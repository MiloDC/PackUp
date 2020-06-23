module PackIt.Pack

open System
open System.Collections.Generic
open System.IO
open System.Text.RegularExpressions
open Newtonsoft.Json
open Newtonsoft.Json.Linq

type DirMap = ResizeArray<Regex * Regex seq>    // directory, files
type EditMap = string * (Regex * string) seq    // file_path, (regex, replacement)

let private reFilePath = Regex "(.+/)([^/]+$)"
let private reOpts = Regex "^[!]+"

let private bprintFiles sb indentation (includes, excludes) =
    let indent = String.replicate indentation "\t"

    Printf.bprintf sb "%sincludes =\n" indent
    includes
    |> Seq.iter (fun (dir, files) ->
        Printf.bprintf sb "%s\t%O =\n" indent dir
        files |> Seq.iter (Printf.bprintf sb "%s\t\t%O\n" indent))

    Printf.bprintf sb "%sexcludes =\n" indent
    excludes
    |> Seq.iter (fun (dir, files) ->
        Printf.bprintf sb "%s\t%O =\n" indent dir
        files |> Seq.iter (Printf.bprintf sb "%s\t\t%O\n" indent))

let private bprintEdits sb indentation editMaps =
    let indent = String.replicate indentation "\t"

    editMaps
    |> Seq.iter (fun (filePath, reRepls) ->
        Printf.bprintf sb "%s%s =\n" indent filePath
        reRepls |> Seq.iter (fun (re, repl) -> Printf.bprintf sb "%s\t%O -> %s\n" indent re repl))

type Platform =
    {
        compression     : Compression
        files           : DirMap * DirMap       // includes, excludes
        edits           : EditMap seq
    }

    member this.bprint sb indentation =
        let indent = String.replicate indentation "\t"

        Printf.bprintf sb "%scompression =\n" indent
        match this.compression with
        | Tar (srcPath, outPath) ->
            Printf.bprintf sb "%s\ttar\n" indent
            Printf.bprintf sb "%s\tsrcPath = %s\n" indent srcPath
            Printf.bprintf sb "%s\toutPath = %s\n" indent outPath
        | Zip (srcPath, outPath, passwd) ->
            Printf.bprintf sb "%s\tzip\n" indent
            Printf.bprintf sb "%s\tsrcPath = %s\n" indent srcPath
            Printf.bprintf sb "%s\toutPath = %s\n" indent outPath
            Printf.bprintf sb "%s\tpassword = \"%s\"\n" indent passwd
        | TarZip (srcPath, outPath, passwd) ->
            Printf.bprintf sb "%s\ttarzip\n" indent
            Printf.bprintf sb "%s\tsrcPath = %s\n" indent srcPath
            Printf.bprintf sb "%s\toutPath = %s\n" indent outPath
            Printf.bprintf sb "%s\tpassword = \"%s\"\n" indent passwd
        | NoCompression -> ()

        Printf.bprintf sb "%sfiles =\n" indent
        bprintFiles sb (indentation + 1) this.files

        Printf.bprintf sb "%sedits =\n" indent
        bprintEdits sb (indentation + 1) this.edits

type Pack =
    {
        version         : string
        rootDir         : DirectoryInfo
        globalFiles     : DirMap * DirMap       // includes, excludes
        globalEdits     : EditMap seq
        platforms       : IDictionary<string, Platform>
    }

    override this.ToString () =
        let sb = System.Text.StringBuilder ()

        Printf.bprintf sb "version = %s\n" this.version

        Printf.bprintf sb "rootDir = %O\n" this.rootDir

        Printf.bprintf sb "globalFiles =\n"
        bprintFiles sb 1 this.globalFiles

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
    |> Array.choose (fun filePath ->
        match (reFilePath.Match filePath).Groups with
        | g when 3 = g.Count -> Some (g.[1].Value, g.[2].Value)
        | _ -> None)
    |> Array.fold (fun (dirMap : Dictionary<string, ResizeArray<string>>) (dir, fileName) ->
        match dirMap.TryGetValue dir with
        | true, fileNamesResizeArray -> fileNamesResizeArray.Add fileName
        | _ ->
            dirMap.[dir] <- ResizeArray<string> ()
            dirMap.[dir].Add fileName
        dirMap)
        (Dictionary<string, ResizeArray<string>> ())
    |> Seq.fold (fun (incl : DirMap, excl) (KeyValue (dir, fNames)) ->
        (if (reOpts.Match dir).Value.Contains '!' then excl else incl)
            .Add (
                reOpts.Replace (dir, "") |> regexOf true true,
                fNames |> Seq.map (fun f -> regexOf true areFileNamesCaseSensitive f)
            )
        incl, excl)
        (DirMap (), DirMap ())

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
            | _ -> DirMap (), DirMap ()

        globalEdits =
            match json with
            | Some j -> editsOf editCaseSens j.["global_edits"]
            | _ -> Seq.empty

        platforms =
            json
            |> Option.bind (fun j ->
                let (JsonMapMap map) = j.["platforms"]
                map
                |> Seq.filter (fun (KeyValue (platform, jObj)) ->
                    platforms'.IsEmpty || platforms'.Contains platform)
                |> Seq.map (fun (KeyValue (platform, jObj)) ->
                    let platDir = sprintf "%s_PACKIT_%c%s%c" rootDirStr dirSep platform dirSep
                    let outPath =
                        match jObj.["outfile"] with JsonString s -> s | _ -> platform
                        |> sprintf "%s%s" rootDirStr
                    let passwd = match jObj.["password"] with JsonString s -> s | _ -> ""

                    platform,
                    {
                        compression =
                            match jObj.["compression"] with
                            | JsonString s when s = "tar" -> Tar (platDir, outPath)
                            | JsonString s when s = "zip" -> Zip (platDir, outPath, passwd)
                            | JsonString s when s = "tarzip" -> TarZip (platDir, outPath, passwd)
                            | _ -> NoCompression

                        files = filesOf filenameCaseSens jObj.["files"]

                        edits = editsOf editCaseSens jObj.["edits"]
                    })
                |> Some)
            |> Option.defaultValue Seq.empty
            |> dict
    }

let pack (pack : Pack) =
    printfn "%O" pack
(*
    let workDirPath = sprintf "%s%c_PACKIT_" pack.rootDir.FullName dirSep
    if Directory.Exists workDirPath then Directory.Delete (workDirPath, true)
    let workDir = Directory.CreateDirectory workDirPath

    let rootDir = normalizePath pack.rootDir.FullName |> sprintf "%s/"
    let allFiles =
        pack.rootDir.GetFiles ("*.*", SearchOption.AllDirectories)
        |> Seq.map (fun fileInfo ->
            let dir = (sprintf "%s/" fileInfo.DirectoryName |> normalizePath).Replace (rootDir, "")
            (if String.IsNullOrEmpty dir then "./" else dir), fileInfo.Name)
    let allIncludes, allExcludes =
        match pack.files.TryGetValue "*" with
        | true, (incl, excl) -> Seq.toArray incl, Seq.toArray excl
        | _ -> Array.empty, Array.empty
    let allEdits =
        match pack.edits.TryGetValue "*" with
        | true, edits -> Seq.toArray edits
        | _ -> Array.empty

    let progressLen = 50
    let platformPrintLen = 8

    pack.compressions.Keys
    |> Seq.iter (fun platform ->
        // 1) Copy files
        let platformDir = workDir.CreateSubdirectory platform
        let includes, excludes =
            match pack.files.TryGetValue platform with
            | true, (incl, excl) ->
                incl |> Seq.toArray |> Array.append allIncludes,
                excl |> Seq.toArray |> Array.append allExcludes
            | _ -> allIncludes, allExcludes
        let copyFiles =
            allFiles
            |> Seq.filter (fun (dir, fileName) ->
                excludes
                |> Array.exists (fun (reDir, reFileNames) ->
                    reDir.IsMatch dir && reFileNames |> Seq.exists (fun re -> re.IsMatch fileName))
                |> not)
            |> Seq.choose (fun (dir, fileName) ->
                if includes |> Array.exists (fun (reDir, reFileNames) ->
                    reDir.IsMatch dir && reFileNames |> Seq.exists (fun re -> re.IsMatch fileName))
                then
                    Some (
                        (sprintf "%s%s%s" rootDir dir fileName) |> Path.GetFullPath,
                        (sprintf "%s/%s%s" platformDir.FullName dir fileName) |> Path.GetFullPath
                    )
                else None)
        let copyCount = Seq.length copyFiles |> single
        let platformPrint =
            platform
                .Substring(0, min platform.Length platformPrintLen)
                .PadRight (platformPrintLen, ' ')
        copyFiles
        |> Seq.iteri (fun i (srcFile, destFile) ->
            let destDir = Path.GetDirectoryName destFile
            if not <| Directory.Exists destDir then Directory.CreateDirectory destDir |> ignore
            File.Copy (srcFile, destFile, true)
            (String.replicate (int ((single (progressLen - 2)) * ((single i) / copyCount))) "#")
                .PadRight (progressLen, '_')
            |> printf "\r%s [%s]" platformPrint)

        // 2) Edit files
        match pack.edits.TryGetValue platform with
        | true, platformEdits -> platformEdits |> Seq.toArray |> Array.append allEdits
        | _ -> allEdits
        |> Array.choose (fun (filePath, editMap) ->
            let fullFilePath =
                (sprintf "%s/%s" platformDir.FullName filePath).Replace ("/./", "/")
                |> Path.GetFullPath
            if File.Exists fullFilePath then Some (fullFilePath, editMap) else None)
        |> Array.iter (fun (filePath, edits) ->
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
        let c = pack.compressions.[platform]
        let outFilePath =
            pack.outFileName.Replace ("{PLATFORM}", platform)
            |> sprintf "%s%s" rootDir
        let cFilePath =
            compress c pack.password outFilePath (sprintf "%s%c" platformDir.FullName dirSep)
        printfn "\ncompressed file = %s" cFilePath
(*
        if (Compression.Tar ||| Compression.Zip) = c then
            compress Compression.Zip outFilePath pack.password cFilePath |> ignore
*)
        printfn "\r%s [%s]" platformPrint (String.replicate progressLen "#"))

    workDir.Delete true
*)
