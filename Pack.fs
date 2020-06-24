module PackIt.Pack

open System
open System.Collections.Generic
open System.IO
open System.Text.RegularExpressions
open Newtonsoft.Json
open Newtonsoft.Json.Linq

type DirMap = (Regex * Regex seq) list          // directory, files
type EditMap = string * (Regex * string) seq    // file_path, (regex, replacement)

let private reFilePath = Regex "(.+/)([^/]+$)"
let private reOpts = Regex "^[!]+"
let [<Literal>] private packItDir = "__PACKIT__"

let private bprintFiles sb indentation (includes, excludes) =
    let indent = String.replicate indentation "\t"

    if List.length includes > 0 then
        Printf.bprintf sb "%sincludes =\n" indent
        includes
        |> List.iter (fun (dir, files) ->
            Printf.bprintf sb "%s\t%O =\n" indent dir
            files |> Seq.iter (Printf.bprintf sb "%s\t\t%O\n" indent))

    if List.length excludes > 0 then
        Printf.bprintf sb "%sexcludes =\n" indent
        excludes
        |> List.iter (fun (dir, files) ->
            Printf.bprintf sb "%s\t%O =\n" indent dir
            files |> Seq.iter (Printf.bprintf sb "%s\t\t%O\n" indent))

let private bprintEdits sb indentation editMaps =
    let indent = String.replicate indentation "\t"

    editMaps
    |> Seq.iter (fun (filePath, reRepls) ->
        Printf.bprintf sb "%s\"%s\" =\n" indent filePath
        reRepls |> Seq.iter (fun (re, repl) ->
            Printf.bprintf sb "%s\t%O -> \"%s\"\n" indent re repl))

type Platform =
    {
        compression     : Compression
        sourceDir       : string
        outPath         : string
        files           : DirMap * DirMap       // includes, excludes
        edits           : EditMap list
    }

    member this.bprint sb indentation =
        let indent = String.replicate indentation "\t"

        Printf.bprintf sb "%scompression = " indent
        match this.compression with
        | Tar -> Printf.bprintf sb "tar\n"
        | Zip password -> Printf.bprintf sb "zip (password = \"%s\")\n" password
        | TarZip password -> Printf.bprintf sb "tarzip (password = \"%s\")\n" password
        | NoCompression -> Printf.bprintf sb "none\n"

        Printf.bprintf sb "%ssourceDir = \"%s\"\n" indent this.sourceDir

        Printf.bprintf sb "%soutPath = \"%s\"\n" indent this.outPath

        if (fst this.files).Length > 0 || (snd this.files).Length > 0 then
            Printf.bprintf sb "%sfiles =\n" indent
            bprintFiles sb (indentation + 1) this.files

        if this.edits.Length > 0 then
            Printf.bprintf sb "%sedits =\n" indent
            bprintEdits sb (indentation + 1) this.edits

type Pack =
    {
        version         : string
        rootDir         : DirectoryInfo
        globalFiles     : DirMap * DirMap       // includes, excludes
        globalEdits     : EditMap list
        platforms       : IDictionary<string, Platform>
    }

    override this.ToString () =
        let sb = System.Text.StringBuilder ()

        Printf.bprintf sb "version = \"%s\"\n" this.version

        Printf.bprintf sb "rootDir = \"%O\"\n" this.rootDir

        if (fst this.globalFiles).Length > 0 || (snd this.globalFiles).Length > 0 then
            Printf.bprintf sb "globalFiles =\n"
            bprintFiles sb 1 this.globalFiles

        if this.globalEdits.Length > 0 then
            Printf.bprintf sb "globalEdits =\n"
            bprintEdits sb 1 this.globalEdits

        Printf.bprintf sb "platforms =\n"
        this.platforms
        |> Seq.iter (fun (KeyValue (name, platform)) ->
            Printf.bprintf sb "\t\"%s\" =\n" name
            platform.bprint sb 2)

        sb.ToString ()

let private filesOf areFileNamesCaseSensitive (JsonStringArray files) =
    files
    |> Seq.choose (fun filePath ->
        match (reFilePath.Match filePath).Groups with
        | g when 3 = g.Count -> Some (g.[1].Value, g.[2].Value)
        | _ -> None)
    |> Seq.fold (fun (dirMap : Dictionary<string, string list>) (dir, fileName) ->
        match dirMap.TryGetValue dir with
        | true, fileNames -> dirMap.[dir] <- fileNames @ [ fileName ]
        | _ -> dirMap.[dir] <- [ fileName ]
        dirMap)
        (Dictionary<string, string list> ())
    |> Seq.fold (fun (incl, excl) (KeyValue (dir, fileNames)) ->
        let entry =
            [
                reOpts.Replace (dir, "") |> regexOf true true,
                fileNames |> Seq.map (fun f -> regexOf true areFileNamesCaseSensitive f)
            ]
        if (reOpts.Match dir).Value.Contains '!' then incl, excl @ entry else incl @ entry, excl)
        ([], [])

let private editsOf areCaseSensitive (JsonArrayMap map) =
    map
    |> Seq.choose (fun (KeyValue (filePath, JsonStringArray jArray)) ->
        let reRepls =
            jArray
            |> Seq.choose (fun str ->
                match str.Split str.[0] with
                | [| ""; reStr; replStr |] -> Some (regexOf false areCaseSensitive reStr, replStr)
                | _ -> None)
        if Seq.length reRepls > 0 then Some (filePath, reRepls) else None)
    |> Seq.toList

let read (platforms' : Set<string>) caseSensitivity jsonFilePath =
    let filenameCaseSens, editCaseSens = (caseSensitivity &&& 1) > 0, (caseSensitivity &&& 2) > 0

    let jsonFullFilePath =
        if File.Exists jsonFilePath then
            (FileInfo jsonFilePath).FullName
        else jsonFilePath

    let json =
        try
            File.ReadAllText jsonFullFilePath |> JObject.Parse |> Some
        with
        | :? JsonReaderException as exc ->
            printfn "Error reading JSON from %s" jsonFullFilePath
            None
        | _ ->
            printfn "Error reading file %s" jsonFullFilePath
            None

    let rootDir' =
        if json.IsSome then (FileInfo jsonFullFilePath).Directory
        else DirectoryInfo (Directory.GetCurrentDirectory ())
    let rootDirStr = sprintf "%s%c" rootDir'.FullName dirSep

    {
        version =
            match json with
            | Some j -> match j.["version"] with JsonString s -> s | _ -> Core.Version
            | _ -> Core.Version

        rootDir = rootDir'

        globalFiles =
            match json with
            | Some j -> filesOf filenameCaseSens j.["global_files"]
            | _ -> [], []

        globalEdits =
            match json with
            | Some j -> editsOf editCaseSens j.["global_edits"]
            | _ -> []

        platforms =
            json
            |> Option.bind (fun j ->
                let (JsonMapMap map) = j.["platforms"]
                map
                |> Seq.filter (fun (KeyValue (platform, jObj)) ->
                    platforms'.IsEmpty || platforms'.Contains platform)
                |> Seq.map (fun (KeyValue (platform, jObj)) ->
                    let outName = match jObj.["out_name"] with JsonString s -> s | _ -> platform
                    let password = match jObj.["password"] with JsonString s -> s | _ -> ""

                    platform,
                    {
                        compression =
                            match jObj.["compression"] with
                            | JsonString s when s = "tar" -> Tar
                            | JsonString s when s = "zip" -> Zip password
                            | JsonString s when s = "tarzip" -> TarZip password
                            | _ -> NoCompression

                        sourceDir =
                            sprintf "%s%s%c%s%c%s%c"
                                rootDirStr packItDir dirSep platform dirSep outName dirSep

                        outPath = sprintf "%s%s" rootDirStr outName

                        files = filesOf filenameCaseSens jObj.["files"]

                        edits = editsOf editCaseSens jObj.["edits"]
                    })
                |> Some)
            |> Option.defaultValue Seq.empty
            |> dict
    }

let pack (pack : Pack) =
//    printfn "%O" pack
    let rootDir = pack.rootDir.FullName |> sprintf "%s/" |> normalizePath
    let srcFiles =
        pack.rootDir.GetFiles ("*.*", SearchOption.AllDirectories)
        |> Array.map (fun fileInfo ->
            let dir = (sprintf "%s/" fileInfo.DirectoryName |> normalizePath).Replace (rootDir, "")
            (if String.IsNullOrEmpty dir then "./" else dir), fileInfo.Name)
    let progressLen = 50
    let platformPrintLen = 8

    pack.platforms
    |> Seq.iter (fun (KeyValue (platformName, platform)) ->
        // 1) Copy files
        if Directory.Exists platform.sourceDir then Directory.Delete (platform.sourceDir, true)
        let platformDir = Directory.CreateDirectory platform.sourceDir
        let includes, excludes =
            fst pack.globalFiles @ fst platform.files,
            snd pack.globalFiles @ snd platform.files
        let copyFiles =
            srcFiles
            |> Array.filter (fun (dir, fileName) ->
                excludes
                |> List.exists (fun (reDir, reFileNames) ->
                    reDir.IsMatch dir && reFileNames |> Seq.exists (fun re -> re.IsMatch fileName))
                |> not)
            |> Array.choose (fun (dir, fileName) ->
                if includes |> List.exists (fun (reDir, reFileNames) ->
                    reDir.IsMatch dir && reFileNames |> Seq.exists (fun re -> re.IsMatch fileName))
                then
                    Some (
                        (sprintf "%s%s%s" rootDir dir fileName) |> Path.GetFullPath,
                        (sprintf "%s/%s%s" platformDir.FullName dir fileName) |> Path.GetFullPath
                    )
                else None)
        let copyCount = Seq.length copyFiles |> single
        let platformPrint =
            platformName
                .Substring(0, min platformName.Length platformPrintLen)
                .PadRight (platformPrintLen, ' ')
        copyFiles
        |> Array.iteri (fun i (srcFile, destFile) ->
            let destDir = Path.GetDirectoryName destFile
            if not <| Directory.Exists destDir then Directory.CreateDirectory destDir |> ignore
            File.Copy (srcFile, destFile, true)
            (String.replicate (int ((single (progressLen - 2)) * ((single i) / copyCount))) "#")
                .PadRight (progressLen, '_')
            |> printf "\r%s [%s]" platformPrint)

        // 2) Edit files
        pack.globalEdits @ platform.edits
        |> List.choose (fun (filePath, editMap) ->
            let fullFilePath =
                (sprintf "%s/%s" platformDir.FullName filePath).Replace ("/./", "/")
                |> Path.GetFullPath
            if File.Exists fullFilePath then Some (fullFilePath, editMap) else None)
        |> List.iter (fun (filePath, edits) ->
            let tmpFilePath = Path.GetTempFileName ()
            let writer = tmpFilePath |> File.CreateText
            let reader = File.OpenText filePath
            while not reader.EndOfStream do
                edits
                |> Seq.fold (fun line (re, repl) -> re.Replace (line, repl)) (reader.ReadLine ())
                |> writer.WriteLine
            reader.Close ()
            writer.Close ()
            File.Move (tmpFilePath, filePath, true))
        printf "\r%s [%s_]" platformPrint (String.replicate (progressLen - 1) "#")

        // 3) Compress files
        let srcDir = sprintf "%s%c" (DirectoryInfo platform.sourceDir).Parent.FullName dirSep
        compress platform.outPath srcDir platform.compression |> ignore
        printfn "\r%s [%s]" platformPrint (String.replicate progressLen "#"))

    let packItRootDir = sprintf "%s%s" rootDir packItDir
    if Directory.Exists packItRootDir then Directory.Delete (packItRootDir, true)
