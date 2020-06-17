[<AutoOpen>]
module PackIt.Core

open System
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
        password    : string
        isLinuxTar  : bool
    }

    override this.ToString () =
        let sb = System.Text.StringBuilder ()

        Printf.bprintf sb "version = %s\n" this.version

        Printf.bprintf sb "files =\n"
        this.files
        |> Seq.iter (fun (platform, incl, excl) ->
            Printf.bprintf sb "\tplatform \"%s\" =\n" platform
            Printf.bprintf sb "\t\tincludes =\n"
            incl |> Seq.iter (fun (dir, files) ->
                Printf.bprintf sb "\t\t\t%O =\n" dir
                files |> Seq.iter (Printf.bprintf sb "\t\t\t\t%O\n"))
            Printf.bprintf sb "\t\texcludes =\n"
            excl |> Seq.iter (fun (dir, files) ->
                Printf.bprintf sb "\t\t\t%O =\n" dir
                files |> Seq.iter (Printf.bprintf sb "\t\t\t\t%O\n")))

        Printf.bprintf sb "edits =\n"
        this.edits
        |> Seq.iter (fun (platform, edits) ->
            Printf.bprintf sb "\tplatform \"%s\" =\n" platform
            edits |> Seq.iter (fun (filePath, reRepls) -> 
                Printf.bprintf sb "\t\t%s =\n" filePath
                reRepls |> Seq.iter (fun (re, repl) ->
                    Printf.bprintf sb "\t\t\t%O -> %s\n" re repl)))

        Printf.bprintf sb "password = %s\n" this.password

        Printf.bprintf sb "linuxTar = %b\n" this.isLinuxTar

        sb.ToString ()

let [<Literal>] private Version = "1.0.0"

let internal reFilePath = Regex "(.+/)([^/]+$)"
let internal reOpts = Regex "^[!]+"

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

let regexOf prepString isCaseSensitive (s : string) =
    Regex (
        if prepString then
            (s.Replace(".", "\\.").Replace ("*", ".*") |> sprintf "^%s$").Replace (".*/$", ".*$")
        else s
        , if isCaseSensitive then RegexOptions.None else RegexOptions.IgnoreCase)

let readPackItFile platform dirsCaseSens filenamesCaseSens editsCaseSens filePath =
    let json =
        try
            File.ReadAllText filePath |> JObject.Parse |> Some
        with
        | :? JsonReaderException as exc ->
            printfn "Error reading JSON from %s" filePath
            None

    {
        version =
            json
            |> Option.bind (fun j -> match j.["version"] with JsonString s -> Some s | _ -> None)
            |> Option.defaultValue Version

        files =
            json
            |> Option.bind (fun j ->
                match j.["files"] with JsonArrayMap map -> Some map | _ -> None)
            |> Option.bind (fun map ->
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
                                    regexOf true dirsCaseSens (reOpts.Replace (dir, "")),
                                    fNames |> Seq.map (fun f -> regexOf true filenamesCaseSens f))
                            incl, excl)
                            (DirMap (), DirMap ())

                    key, includes, excludes)
                |> Some)
            |> Option.defaultValue Seq.empty

        edits =
            json
            |> Option.bind (fun j -> match j.["edits"] with JsonMapMap map -> Some map | _ -> None)
            |> Option.bind (fun map ->
                map
                |> Seq.choose (fun (KeyValue (platform, edits')) ->
                    match edits' with
                    | JsonArrayMap map ->
                        let editMap =
                            map
                            |> Seq.choose (fun (KeyValue (filePath, JsonStringArray jArray)) ->
                                let reRepls =
                                    jArray
                                    |> Seq.choose (fun str ->
                                        match str.Split (str.[0], StringSplitOptions.RemoveEmptyEntries) with
                                        | [| reStr; replStr |] -> Some (Regex reStr, replStr)
                                        | _ -> None)
                                if Seq.length reRepls > 0 then
                                    Some (filePath, reRepls)
                                else None)
                        Some (platform, editMap)
                    | _ -> None)
                |> Some)
            |> Option.defaultValue Seq.empty

        password =
            json
            |> Option.bind (fun j -> match j.["password"] with JsonString s -> Some s | _ -> None)
            |> Option.defaultValue ""

        isLinuxTar =
            json
            |> Option.bind (fun j -> match j.["linux_tar"] with JsonBool b -> Some b | _ -> None)
            |> Option.defaultValue false
    }
