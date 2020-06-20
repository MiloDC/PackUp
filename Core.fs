[<AutoOpen>]
module PackIt.Core

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
        platforms       : string seq
        rootDir         : DirectoryInfo
        outFilePrefix   : string
        files           : IDictionary<string, DirMap * DirMap>  // platform, (includes, excludes)
        edits           : IDictionary<string, EditMap seq>      // platform, edits
        password        : string
        isLinuxTar      : bool
    }

    override this.ToString () =
        let sb = System.Text.StringBuilder ()

        Printf.bprintf sb "version = %s\n" this.version

        Printf.bprintf sb "platforms = %A\n" this.platforms

        Printf.bprintf sb "rootDir = %O\n" this.rootDir

        Printf.bprintf sb "outFile = %s\n" this.outFilePrefix

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

        Printf.bprintf sb "password = %s\n" this.password

        Printf.bprintf sb "linuxTar = %b\n" this.isLinuxTar

        sb.ToString ()

let [<Literal>] private Version = "1.0.0"
let private dirSep = Path.DirectorySeparatorChar
let private reFilePath = Regex "(.+/)([^/]+$)"
let private reOpts = Regex "^[!]+"

let private normalizePath (path : string) = path.Replace (dirSep, '/')

let regexOf prepString isCaseSensitive (s : string) =
    Regex (
        if prepString then
            (s.Replace(".", "\\.").Replace ("*", ".*") |> sprintf "^%s$").Replace (".*/$", ".*$")
        else s
        , if isCaseSensitive then RegexOptions.None else RegexOptions.IgnoreCase)

// BEGIN JSON partial acive patterns

let internal (|JsonBool|_|) (jToken : JToken) =
    if (null <> jToken) && (jToken.Type = JTokenType.Boolean) then
        Some (jToken.Value<bool> ())
    else None

let internal (|JsonString|_|) (jToken : JToken) =
    if (null <> jToken) && (jToken.Type = JTokenType.String) then
        Some (jToken.Value<string> ())
    else None

let internal (|JsonArrayMap|_|) (jToken : JToken) =
    if (null <> jToken) && (jToken.Type = JTokenType.Object) then
        downcast (jToken :?> JObject)
        |> Seq.choose (fun (KeyValue (key, jToken)) ->
            if jToken.Type = JTokenType.Array then Some (key, jToken :?> JArray) else None)
        |> dict
        |> Some
    else None

let internal (|JsonMapMap|_|) (jToken : JToken) =
    if (null <> jToken) && (jToken.Type = JTokenType.Object) then
        downcast (jToken :?> JObject)
        |> Seq.choose (fun (KeyValue (key, jToken)) ->
            if jToken.Type = JTokenType.Object then Some (key, jToken :?> JObject) else None)
        |> dict
        |> Some
    else None

// END JSON partial acive patterns

let internal (|JsonStringArray|) (jToken : JToken) =
    if (null <> jToken) && (jToken.Type = JTokenType.Array) then
        jToken.Children ()
        |> Seq.choose (fun t ->
            if t.Type = JTokenType.String then
                let str = t.Value<string> ()
                if not <| String.IsNullOrEmpty str then Some str else None
            else None)
    else Seq.empty

let readPackItFile (platforms' : Set<string>) caseSensitivity jsonFilePath =
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
            |> Option.defaultValue Version

        platforms =
            json
            |> Option.bind (fun j ->
                let (JsonStringArray platformArray) = j.["platforms"]
                platformArray
                |> Seq.filter (fun p -> platforms'.IsEmpty || platforms'.Contains p)
                |> Some)
            |> Option.defaultValue Seq.empty

        rootDir = rootDir'

        outFilePrefix =
            json
            |> Option.bind (fun j ->
                match j.["outfile"] with
                | JsonString s ->
                    sprintf "%s%c%s" rootDir'.FullName dirSep s |> Some
                | _ -> None)
            |> Option.defaultValue ""

        files =
            json
            |> Option.bind (fun j ->
                match j.["files"] with JsonArrayMap map -> Some map | _ -> None)
            |> Option.bind (fun map ->
                map
                |> Seq.filter (fun (KeyValue (platform, _)) ->
                    platforms'.IsEmpty || platform.Equals "*" || platforms'.Contains platform)
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

                    platform, (includes, excludes))
                |> dict
                |> Some)
            |> Option.defaultValue (dict Seq.empty)

        edits =
            json
            |> Option.bind (fun j -> match j.["edits"] with JsonMapMap map -> Some map | _ -> None)
            |> Option.bind (fun map ->
                map
                |> Seq.filter (fun (KeyValue (platform, _)) ->
                    platforms'.IsEmpty || platform.Equals "*" || platforms'.Contains platform)
                |> Seq.choose (fun (KeyValue (platform, edits')) ->
                    match edits' with
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
                        Some (platform, editMap)
                    | _ -> None)
                |> dict
                |> Some)
            |> Option.defaultValue (dict Seq.empty)

        password =
            json
            |> Option.bind (fun j -> match j.["password"] with JsonString s -> Some s | _ -> None)
            |> Option.defaultValue ""

        isLinuxTar =
            json
            |> Option.bind (fun j -> match j.["linux_tar"] with JsonBool b -> Some b | _ -> None)
            |> Option.defaultValue false
    }

let packUp (pack : Pack) =
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
//        |> Seq.take 100
    let allIncludes, allExcludes =
        match pack.files.TryGetValue "*" with
        | true, (incl, excl) -> Seq.toArray incl, Seq.toArray excl
        | false, _ -> Array.empty, Array.empty

    let printPathLen = 64

    pack.platforms
    |> Seq.iter (fun platform ->
        let platformDir = workDir.CreateSubdirectory platform
        let includes, excludes =
            match pack.files.TryGetValue platform with
            | true, (incl, excl) ->
                incl |> Seq.toArray |> Array.append allIncludes,
                excl |> Seq.toArray |> Array.append allExcludes
            | false, _ -> allIncludes, allExcludes

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
        |> Seq.iter (fun (srcFile, destFile) ->
            let printPath = srcFile.Replace (Path.GetFullPath rootDir, "")
            printPath
                .Substring(0, if printPath.Length < (printPathLen + 1) then printPath.Length else (printPathLen - 3))
                .PadRight((if printPath.Length < (printPathLen + 1) then printPath.Length else printPathLen), '.')
                .PadRight (printPathLen, ' ')
            |> printf "\r%s"

            let destDir = Path.GetDirectoryName destFile
            if not <| Directory.Exists destDir then Directory.CreateDirectory destDir |> ignore
            File.Copy (srcFile, destFile, true)))

//    workDir.Delete true
