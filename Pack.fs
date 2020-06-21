module PackIt.Pack

open System
open System.Collections.Generic
open System.IO
open System.Text.RegularExpressions
open Newtonsoft.Json
open Newtonsoft.Json.Linq

type DirMap = ResizeArray<Regex * Regex seq>    // directory, files
type EditMap = string * (Regex * string) seq    // file_path, (regex, replacement)

type Pack =
    {
        version         : string
        outFile         : string
        compressions    : IDictionary<string, Compression>      // platform, compression
        password        : string
        rootDir         : DirectoryInfo
        files           : IDictionary<string, DirMap * DirMap>  // platform, (includes, excludes)
        edits           : IDictionary<string, EditMap seq>      // platform, edits
    }

    override this.ToString () =
        let sb = System.Text.StringBuilder ()

        Printf.bprintf sb "version = %s\n" this.version

        Printf.bprintf sb "outFile = %s\n" this.outFile

        Printf.bprintf sb "compressions =\n"
        this.compressions
        |> Seq.iter (fun (KeyValue (platform, compression)) ->
            Printf.bprintf sb "\tplatform \"%s\" = \"%s\"\n"
                platform
                (match compression with
                | c when c = (Compression.Tar ||| Compression.Zip) -> "tarzip"
                | Compression.Tar -> "tar"
                | _ -> "zip"))

        Printf.bprintf sb "password = %s" this.password

        Printf.bprintf sb "rootDir = %O\n" this.rootDir

(*
        Printf.bprintf sb "files =\n"
        this.files
        |> Seq.iter (fun (KeyValue (platform, (includes, excludes))) ->
            Printf.bprintf sb "\tplatform \"%s\" =\n" platform
            Printf.bprintf sb "\t\tincludes =\n"
            includes |> Seq.iter (fun (dir, files) ->
                Printf.bprintf sb "\t\t\t%O =\n" dir
                files |> Seq.iter (Printf.bprintf sb "\t\t\t\t%O\n"))
            Printf.bprintf sb "\t\texcludes =\n"
            excludes |> Seq.iter (fun (dir, files) ->
                Printf.bprintf sb "\t\t\t%O =\n" dir
                files |> Seq.iter (Printf.bprintf sb "\t\t\t\t%O\n")))

        Printf.bprintf sb "edits =\n"
        this.edits
        |> Seq.iter (fun (KeyValue (platform, edits)) ->
            Printf.bprintf sb "\tplatform \"%s\" =\n" platform
            edits |> Seq.iter (fun (filePath, reRepls) -> 
                Printf.bprintf sb "\t\t%s =\n" filePath
                reRepls |> Seq.iter (fun (re, repl) ->
                    Printf.bprintf sb "\t\t\t%O -> %s\n" re repl)))
*)

        sb.ToString ()

let read (platforms : Set<string>) caseSensitivity jsonFilePath =
    let dirsCaseSens, filenamesCaseSens, editsCaseSens =
        (caseSensitivity &&& 1) > 0, (caseSensitivity &&& 2) > 0, (caseSensitivity &&& 4) > 0

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

    {
        version =
            json
            |> Option.bind (fun j -> match j.["version"] with JsonString s -> Some s | _ -> None)
            |> Option.defaultValue Core.Version

        outFile =
            json
            |> Option.bind (fun j ->
                match j.["outfile"] with
                | JsonString s ->
                    sprintf "%s%c%s" rootDir'.FullName dirSep s |> Some
                | _ -> None)
            |> Option.defaultValue ""

        compressions =
            json
            |> Option.bind (fun j ->
                let (JsonStringMap compressionMap) = j.["compressions"]
                compressionMap
                |> Seq.choose (fun (KeyValue (platform, CompressionValue compression)) ->
                    if
                        (not <| String.IsNullOrWhiteSpace platform)
                        && (platforms.IsEmpty || platforms.Contains platform)
                    then Some (platform, compression)
                    else None)
                |> dict
                |> Some)
            |> Option.defaultValue (dict Seq.empty)

        password =
            json
            |> Option.bind (fun j -> match j.["password"] with JsonString s -> Some s | _ -> None)
            |> Option.defaultValue ""

        rootDir = rootDir'

        files =
            json
            |> Option.bind (fun j ->
                match j.["files"] with JsonArrayMap map -> Some map | _ -> None)
            |> Option.bind (fun map ->
                map
                |> Seq.filter (fun (KeyValue (platform, _)) ->
                    platforms.IsEmpty || platform.Equals "*" || platforms.Contains platform)
                |> Seq.map (fun (KeyValue (platform, JsonStringArray filePaths)) ->
                    let includes, excludes =
                        filePaths
                        |> Seq.choose (fun filePath ->
                            match (reFilePath.Match filePath).Groups with
                            | g when 3 = g.Count -> Some (g.[1].Value, g.[2].Value)
                            | _ -> None)
                        |> Seq.fold (fun (dirMap : Dictionary<string, ResizeArray<string>>) (dir, fileName) ->
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
                                    reOpts.Replace (dir, "") |> regexOf true dirsCaseSens,
                                    fNames |> Seq.map (fun f -> regexOf true filenamesCaseSens f)
                                )
                            incl, excl)
                            (DirMap (), DirMap ())

                    platform.ToLower (), (includes, excludes))
                |> dict
                |> Some)
            |> Option.defaultValue (dict Seq.empty)

        edits =
            json
            |> Option.bind (fun j -> match j.["edits"] with JsonMapMap map -> Some map | _ -> None)
            |> Option.bind (fun map ->
                map
                |> Seq.filter (fun (KeyValue (platform, _)) ->
                    platforms.IsEmpty || platform.Equals "*" || platforms.Contains platform)
                |> Seq.choose (fun (KeyValue (platform, jEditsObj)) ->
                    match jEditsObj with
                    | JsonArrayMap arrayMap ->
                        let editMap =
                            arrayMap
                            |> Seq.choose (fun (KeyValue (filePath, JsonStringArray jArray)) ->
                                let reRepls =
                                    jArray
                                    |> Seq.choose (fun str ->
                                        match str.Split str.[0] with
                                        | [| ""; reStr; replStr |] ->
                                            Some (regexOf false editsCaseSens reStr, replStr)
                                        | _ -> None)
                                if Seq.length reRepls > 0 then
                                    Some (filePath, reRepls)
                                else None)
                        Some (platform.ToLower (), editMap)
                    | _ -> None)
                |> dict
                |> Some)
            |> Option.defaultValue (dict Seq.empty)
    }

let pack (pack : Pack) =
//    printfn "%O" pack
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
        platformDir.FullName
        |> compress
            pack.compressions.[platform]
            (pack.outFile.Replace ("{PLATFORM}", platform))
            pack.password
        printfn "\r%s [%s]" platformPrint (String.replicate progressLen "#"))

    workDir.Delete true
