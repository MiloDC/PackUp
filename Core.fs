[<AutoOpen>]
module PackIt.Core

open System.Collections.Generic
open System.IO
open System.Text.RegularExpressions
open Newtonsoft.Json
open Newtonsoft.Json.Linq

type DirMap = ResizeArray<Regex * Regex seq>    // directory, files
type EditMap = string * (Regex * string) seq    // file, (regex, replacement)
type StringStringRszArrMap = Dictionary<string, ResizeArray<string>>

type PackIt =
    {
        version     : string
        files       : (string * DirMap * DirMap) seq    // platform, includes, excludes
        edits       : (string * EditMap seq) seq        // platform, edits
    }

let [<Literal>] private Version = "1.0.0"
let private reFilePath = Regex "(.+/)([^/]+$)"
let private reOpts = Regex "^[!]+"

// BEGIN JSON partial acive patterns

let private (|JsonString|_|) (jToken : JToken) =
    if (null <> jToken) && (jToken.Type = JTokenType.String) then
        Some (jToken.Value<string> ())
    else None

let private (|JsonArrayMap|_|) (jToken : JToken) =
    if (null <> jToken) && (jToken.Type = JTokenType.Object) then
        downcast (jToken :?> JObject)
        |> Seq.choose (fun (KeyValue (key, jToken)) ->
            if jToken.Type = JTokenType.Array then Some (key, jToken :?> JArray) else None)
        |> dict
        |> Some
    else None

let private (|JsonMapMap|_|) (jToken : JToken) =
    if (null <> jToken) && (jToken.Type = JTokenType.Object) then
        downcast (jToken :?> JObject)
        |> Seq.choose (fun (KeyValue (key, jToken)) ->
            if jToken.Type = JTokenType.Object then Some (key, jToken :?> JObject) else None)
        |> dict
        |> Some
    else None

// END JSON partial acive patterns

let private (|JsonStringArray|) (jToken : JToken) =
    if (null <> jToken) && (jToken.Type = JTokenType.Array) then
        jToken.Children ()
        |> Seq.choose (fun t ->
            if t.Type = JTokenType.String then Some (t.Value<string> ()) else None)
    else Seq.empty

let regexOf (s : string) isCaseSensitive =
    Regex (
        ((s.Replace (".", "\\.")).Replace ("*", ".*") |> sprintf "^%s$").Replace (".*/$", ".*$"),
        if isCaseSensitive then RegexOptions.None else RegexOptions.IgnoreCase)

let readPackItFile filePath dirsCaseSens filenamesCaseSens =
    let json =
        try
            File.ReadAllText filePath |> JObject.Parse |> Some
        with
        | :? JsonReaderException as exc -> None

    {
        version =
            json
            |> Option.bind (fun j ->
                match j.["version"] with JsonString s -> s | _ -> Version
                |> Some)
            |> Option.defaultValue Version

        files =
            json
            |> Option.bind (fun j ->
                match j.["files"] with
                | JsonArrayMap map ->
                    map
                    |> Seq.map (fun (KeyValue (key, JsonStringArray filePaths)) ->
                        let includes, excludes =
                            filePaths
                            |> Seq.choose (fun filePath ->
                                match (reFilePath.Match filePath).Groups with
                                | g when 3 = g.Count -> Some (g.[1].Value, g.[2].Value)
                                | _ -> None)
                            |> Seq.fold (fun (tmpMap : StringStringRszArrMap) (dir, fileName) ->
                                match tmpMap.TryGetValue dir with
                                | true, ra -> ra.Add fileName
                                | _ ->
                                    tmpMap.[dir] <- ResizeArray<string> ()
                                    tmpMap.[dir].Add fileName
                                tmpMap)
                                (StringStringRszArrMap ())
                            |> Seq.fold (fun (incl : DirMap, excl) (KeyValue (dir, fNames)) ->
                                (if (reOpts.Match dir).Value.Contains '!' then excl else incl)
                                    .Add (
                                        regexOf (reOpts.Replace (dir, "")) dirsCaseSens,
                                        fNames |> Seq.map (fun f -> regexOf f filenamesCaseSens))
                                incl, excl)
                                (DirMap (), DirMap ())

                        key, includes, excludes)
                | _ -> Seq.empty
                |> Some)
            |> Option.defaultValue Seq.empty

        edits =
            json
            |> Option.bind (fun j ->
                match j.["edits"] with
                | JsonMapMap map ->
                    map
                    |> Seq.choose (fun (KeyValue (key, edits')) ->
                        match edits' with
                        | JsonArrayMap map ->
                            //
                        | _ -> false)
                    |> Seq (
                        )
                | _ -> Seq.empty
                |> Some)
            |> Option.defaultValue Seq.empty
    }
