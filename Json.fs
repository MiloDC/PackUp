[<RequireQualifiedAccess>]
module PackIt.Json

open System
open System.Collections.Generic
open Newtonsoft.Json.Linq
open Platform
open Pack

let private (|JsonString|_|) (jToken : JToken) =
    if (null <> jToken) && (jToken.Type = JTokenType.String) then
        Some (jToken.Value<string> ())
    else None

let private (|JsonStringArray|) (jToken : JToken) =
    if (null <> jToken) && (jToken.Type = JTokenType.Array) then
        jToken.Children ()
        |> Seq.choose (fun t ->
            if t.Type = JTokenType.String then
                let str = t.Value<string> ()
                if not <| String.IsNullOrEmpty str then Some str else None
            else None)
    else Seq.empty

let private (|JsonArrayMap|) (jToken : JToken) =
    if (null <> jToken) && (jToken.Type = JTokenType.Object) then
        downcast (jToken :?> JObject)
        |> Seq.choose (fun (KeyValue (key, jToken)) ->
            if jToken.Type = JTokenType.Array then Some (key, jToken :?> JArray) else None)
    else Seq.empty
    |> dict

let private (|JsonMapMap|) (jToken : JToken) =
    if (null <> jToken) && (jToken.Type = JTokenType.Object) then
        downcast (jToken :?> JObject)
        |> Seq.choose (fun (KeyValue (key, jToken)) ->
            if jToken.Type = JTokenType.Object then Some (key, jToken :?> JObject) else None)
    else Seq.empty
    |> dict

let private filesOf areFileNamesCaseSensitive (JsonStringArray files) =
    files
    |> Seq.choose (fun filePath ->
        match (RE.filePath.Match filePath).Groups with
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
                RE.filePathOptions.Replace (dir, "") |> RE.regexOf true true,
                fileNames |> Seq.map (fun f -> RE.regexOf true areFileNamesCaseSensitive f)
            ]
        if (RE.filePathOptions.Match dir).Value.Contains '-' then
            incl, excl @ entry
        else
            incl @ entry, excl)
        ([], [])

let private editsOf caseSensitive (JsonArrayMap map) =
    map
    |> Seq.choose (fun (KeyValue (filePath, JsonStringArray jArray)) ->
        let reRepls =
            jArray
            |> Seq.choose (fun str ->
                match str.Split str.[0] with
                | [| ""; reStr; replStr |] -> Some (RE.regexOf false caseSensitive reStr, replStr)
                | _ -> None)
        if Seq.length reRepls > 0 then Some (filePath, reRepls) else None)
    |> Seq.toList

let readFile (platforms' : Set<string>) caseSensitivity jsonFilePath =
    let filenameCaseSens, editCaseSens = (caseSensitivity &&& 1) > 0, (caseSensitivity &&& 2) > 0

    let jsonFullFilePath =
        if IO.File.Exists jsonFilePath then
            (IO.FileInfo jsonFilePath).FullName
        else jsonFilePath

    let json =
        try
            IO.File.ReadAllText jsonFullFilePath |> Newtonsoft.Json.Linq.JObject.Parse |> Some
        with
        | :? Newtonsoft.Json.JsonReaderException as exc ->
            printfn "Error reading JSON from %s" jsonFullFilePath
            None
        | _ ->
            printfn "Error reading file %s" jsonFullFilePath
            None

    let rootDir' =
        if json.IsSome then (IO.FileInfo jsonFullFilePath).Directory
        else IO.DirectoryInfo (IO.Directory.GetCurrentDirectory ())

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
                            | JsonString s when s = "tar" -> Compression.Tar
                            | JsonString s when s = "zip" -> Compression.Zip password
                            | JsonString s when s = "tarzip" -> Compression.TarZip password
                            | _ -> Compression.None

                        sourceDir =
                            sprintf "%s%s%c%s%c"
                                (packItDirOf rootDir'.FullName) platform dirSep outName dirSep

                        outPath = sprintf "%s%c%s" rootDir'.FullName dirSep outName

                        files = filesOf filenameCaseSens jObj.["files"]

                        edits = editsOf editCaseSens jObj.["edits"]
                    })
                |> Some)
            |> Option.defaultValue Seq.empty
            |> dict
    }
