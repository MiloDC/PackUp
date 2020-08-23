[<RequireQualifiedAccess>]
module PackUp.Application.Json

open System
open System.Text.RegularExpressions
open Newtonsoft.Json.Linq
open PackUp

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

[<RequireQualifiedAccess>]
module private RE =
    let private dotSlashOrAsteriskStart = Regex @"^\./|^\*"
    let filePath = Regex @"^[+-]*.+"
    let filePathOptions = Regex @"^[+-]+"

    let regexOf prepareString isCaseSensitive (s : string) =
        Regex (
            if prepareString then
                (s.Replace(".", @"\.").Replace("*", ".*").Replace ("?", ".")
                |> sprintf "^%s%s$" (if dotSlashOrAsteriskStart.IsMatch s then "" else @"\./"))
                    .Replace (".*/$", ".*$")
            else s
            , if isCaseSensitive then RegexOptions.None else RegexOptions.IgnoreCase)

let private filesOf caseSensitive (JsonStringArray files) =
    files
    |> Seq.filter RE.filePath.IsMatch
    |> Seq.fold (fun (wl, bl, incl) filePath ->
        let re = RE.filePathOptions.Replace (filePath, "") |> RE.regexOf true caseSensitive
        let opts = (RE.filePathOptions.Match filePath).Value
        if opts.Contains '+' then
            wl @ [ re ], bl, incl
        elif opts.Contains '-' then
            wl, bl @ [ re ], incl
        else
            wl, bl, incl @ [ re ])
        ([], [], [])

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

let readFile (platforms : Set<string>) caseSensitivity jsonFilePath =
    let json =
        try
            IO.File.ReadAllText jsonFilePath |> Newtonsoft.Json.Linq.JObject.Parse |> Some
        with
        | :? Newtonsoft.Json.JsonReaderException as exc ->
            printfn "Error reading JSON from %s" jsonFilePath
            None
        | _ ->
            printfn "Error reading file %s" jsonFilePath
            None

    let filenameCaseSens, editCaseSens = (caseSensitivity &&& 1) > 0, (caseSensitivity &&& 2) > 0

    let globalWL, globalBL, globalIncl =
        match json with
        | Some j -> filesOf filenameCaseSens j.["global_files"]
        | _ -> [], [], []

    let globalEdits =
        match json with
        | Some j -> editsOf editCaseSens j.["global_edits"]
        | _ -> []

    let rootDirectory =
        if json.IsSome then (IO.FileInfo jsonFilePath).Directory
        else IO.DirectoryInfo (IO.Directory.GetCurrentDirectory ())

    json
    |> Option.bind (fun j ->
        let (JsonMapMap map) = j.["platforms"]
        map
        |> Seq.filter (fun (KeyValue (p, _)) -> platforms.IsEmpty || platforms.Contains p)
        |> Seq.map (fun (KeyValue (platform, jObj)) ->
            let tgtName = match jObj.["target_name"] with JsonString s -> s | _ -> platform
            let password = match jObj.["password"] with JsonString s -> s | _ -> null

            {
                description = platform

                rootDir = rootDirectory.FullName

                compression =
                    match jObj.["compression"] with
                    | JsonString s when s = "tar" -> Tar
                    | JsonString s when s = "zip" -> Zip password
                    | JsonString s when s = "tarzip" -> TarZip password
                    | _ -> NoCompression

                targetPath = sprintf "%s/%s" (normalizePath rootDirectory.FullName) tgtName

                files =
                    let wl, bl, incl = filesOf filenameCaseSens jObj.["files"]
                    globalWL @ wl, globalBL @ bl, globalIncl @ incl

                edits = globalEdits @ editsOf editCaseSens jObj.["edits"]
            })
        |> Some)
    |> Option.defaultValue Seq.empty
