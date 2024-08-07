﻿[<RequireQualifiedAccess>]
module PackUp.Application.Json

open System
open System.Text.Json
open System.Text.RegularExpressions
open PackUp

let private (|JArray|_|) (jsonElement : JsonElement, propertyName : string) =
    match jsonElement.TryGetProperty propertyName with
    | true, el when el.ValueKind = JsonValueKind.Array -> el.EnumerateArray () |> Some
    | _ -> None

let private (|JObj|_|) (jsonElement : JsonElement, propertyName : string) =
    match jsonElement.TryGetProperty propertyName with
    | true, el when el.ValueKind = JsonValueKind.Object -> el.EnumerateObject () |> Some
    | _ -> None

let private (|JString|_|) (jsonElement : JsonElement, propertyName : string) =
    match jsonElement.TryGetProperty propertyName with
    | true, el when el.ValueKind = JsonValueKind.String -> el.GetString () |> Some
    | _ -> None

let private (|JKeyValue|) (jsonProperty : JsonProperty) = jsonProperty.Name, jsonProperty.Value

let private (|JStringArray|) =
    function
    | JArray jArray ->
        jArray
        |> Seq.choose (fun el ->
            if JsonValueKind.String = el.ValueKind then
                let str = el.GetString ()
                if not <| String.IsNullOrEmpty str then Some str else None
            else None)
    | _ -> Seq.empty

let private (|JArrayMap|) =
    function
    | JObj jObj ->
        jObj
        |> Seq.choose (fun (JKeyValue (k, v)) ->
            if JsonValueKind.Array = v.ValueKind then Some (k, v) else None)
        |> dict
    | _ -> dict Seq.empty

let private (|JObjectMap|) =
    function
    | JObj jObj ->
        jObj
        |> Seq.choose (fun (JKeyValue (k, v)) ->
            if JsonValueKind.Object = v.ValueKind then Some (k, v) else None)
        |> dict
    | _ -> dict Seq.empty

let private (|JFile|) jsonFilePath =
    try
        Some (jsonFilePath, (IO.File.ReadAllText jsonFilePath |> JsonDocument.Parse).RootElement)
    with
    | :? JsonException as exc ->
        printfn $"Invalid JSON in %s{jsonFilePath}"
        None
    | exc ->
        printfn $"Error reading file %s{jsonFilePath}: %s{exc.Message}"
        None

let private reFilePath = Regex @"^[+-]*.+"
let private reFilePathOptions = Regex @"^[+-]+"

let private filesOf caseSensitive (JStringArray files) =
    files
    |> Seq.filter reFilePath.IsMatch
    |> Seq.fold
        (fun (wl, bl, incl) filePath ->
            let re =
                reFilePathOptions.Replace (filePath, "")
                |> RE.ofString true true caseSensitive
            let opts = (reFilePathOptions.Match filePath).Value
            if opts.Contains '+' then
                wl @ [ re ], bl, incl
            elif opts.Contains '-' then
                wl, bl @ [ re ], incl
            else
                wl, bl, incl @ [ re ])
        ([], [], [])

let private editsOf caseSensitive (JArrayMap map) =
    map
    |> Seq.choose (fun (KeyValue (filePath, jArray)) ->
        let reRepls =
            jArray.EnumerateArray ()
            |> Seq.choose (fun jElement ->
                if JsonValueKind.String = jElement.ValueKind then
                    let str = jElement.GetString ()
                    match str.Split str.[0] with
                    | [| ""; reStr; replStr |] ->
                        Some (RE.ofString false true caseSensitive reStr, replStr)
                    | _ -> None
                else None)
        if Seq.length reRepls > 0 then Some (filePath, reRepls) else None)
    |> Seq.toList

let internal readFile (configs : Set<string>) caseSens (JFile jFile) =
    jFile
    |> Option.map (fun (jsonFilePath, jRoot) ->
        let filenameCaseSens, editCaseSens = (caseSens &&& 1) > 0, (caseSens &&& 2) > 0
        let globalWL, globalBL, globalIncl = filesOf filenameCaseSens (jRoot, "global_files")
        let globalEdits = editsOf editCaseSens (jRoot, "global_edits")
        let rootDirectory = (IO.FileInfo jsonFilePath).Directory

        let (JObjectMap map) = jRoot, "configurations"
        map
        |> Seq.filter (fun (KeyValue (c, _)) -> configs.IsEmpty || configs.Contains c)
        |> Seq.map (fun (KeyValue (config, jObj)) ->
            let tgtName = match jObj, "target_name" with JString s -> s | _ -> config
            let tgtRoot = match jObj, "target_root" with JString s -> s | _ -> ""
            let password = match jObj, "password" with JString s -> s | _ -> ""

            {
                description = config
                rootDir = rootDirectory.FullName
                compression =
                    match jObj, "compression" with
                    | JString s when s = "tar" -> Tar
                    | JString s when s = "zip" -> Zip password
                    | JString s when s = "tarzip" -> TarZip password
                    | _ -> NoCompression
                targetPath = $"{normalizeFullPath rootDirectory.FullName}/{tgtName}"
                targetRoot = tgtRoot
                files =
                    let wl, bl, incl = filesOf filenameCaseSens (jObj, "files")
                    globalWL @ wl, globalBL @ bl, globalIncl @ incl

                edits = globalEdits @ editsOf editCaseSens (jObj, "edits")
                newLine =
                    match jObj, "newline" with JString s -> NewLine.ofString s | _ -> System
            })
        |> List.ofSeq)
    |> Option.defaultValue List.empty
