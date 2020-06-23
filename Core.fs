[<AutoOpen>]
module PackIt.Core

open System
open System.IO
open System.Text.RegularExpressions
open Newtonsoft.Json.Linq
open ICSharpCode.SharpZipLib

let [<Literal>] Version = "1.0.0"

let internal dirSep = Path.DirectorySeparatorChar

type Compression =
    | Tar of srcPath : string * outFilePath : string
    | Zip of srcPath : string * outFilePath : string * password : string
    | TarZip of srcPath : string * outFilePath : string * password : string
    | NoCompression

let internal (|JsonStringArray|) (jToken : JToken) =
    if (null <> jToken) && (jToken.Type = JTokenType.Array) then
        jToken.Children ()
        |> Seq.choose (fun t ->
            if t.Type = JTokenType.String then
                let str = t.Value<string> ()
                if not <| String.IsNullOrEmpty str then Some str else None
            else None)
        |> Seq.toArray
    else Array.empty

let internal (|JsonArrayMap|) (jToken : JToken) =
    if (null <> jToken) && (jToken.Type = JTokenType.Object) then
        downcast (jToken :?> JObject)
        |> Seq.choose (fun (KeyValue (key, jToken)) ->
            if jToken.Type = JTokenType.Array then Some (key, jToken :?> JArray) else None)
    else Seq.empty
    |> dict

let internal (|JsonMapMap|) (jToken : JToken) =
    if (null <> jToken) && (jToken.Type = JTokenType.Object) then
        downcast (jToken :?> JObject)
        |> Seq.choose (fun (KeyValue (key, jToken)) ->
            if jToken.Type = JTokenType.Object then Some (key, jToken :?> JObject) else None)
    else Seq.empty
    |> dict

let internal (|JsonStringMap|) (jToken : JToken) =
    if (null <> jToken) && (jToken.Type = JTokenType.Object) then
        downcast (jToken :?> JObject)
        |> Seq.choose (fun (KeyValue (key, jToken)) ->
            if jToken.Type = JTokenType.String then Some (key, jToken.Value<string> ()) else None)
    else Seq.empty
    |> dict

let internal (|JsonString|_|) (jToken : JToken) =
    if (null <> jToken) && (jToken.Type = JTokenType.String) then
        Some (jToken.Value<string> ())
    else None

let internal normalizePath (path : string) = path.Replace (dirSep, '/')

let internal regexOf prepString isCaseSensitive (s : string) =
    Regex (
        if prepString then
            (s.Replace(".", "\\.").Replace ("*", ".*") |> sprintf "^%s$").Replace (".*/$", ".*$")
        else s
        , if isCaseSensitive then RegexOptions.None else RegexOptions.IgnoreCase)

(*
let internal compress compression password outFilePath srcPath =
    match compression with
    | c when (int (Compression.Tar &&& c) > 0) ->
        // tar
        let tarFilePath = outFilePath |> sprintf "%s.tar.gz" |> Path.GetFullPath
        File.Delete tarFilePath
        let tmpFilePath = Path.GetFileName tarFilePath |> sprintf "%s%s" srcPath |> Path.GetFullPath
        printfn "tmpFilePath = %s" tmpFilePath
//        use stream = new GZip.GZipOutputStream (File.Create tarFilePath)
        let stream = new GZip.GZipOutputStream (File.Create tmpFilePath)
        let tar = Tar.TarArchive.CreateOutputTarArchive (stream, Tar.TarBuffer.DefaultBlockFactor)
        tar.RootPath <- srcPath

        (srcPath |> Path.GetFullPath |> DirectoryInfo).GetFiles ("*", SearchOption.AllDirectories)
        |> Array.iter (fun f ->
            if f.FullName <> tmpFilePath then
                tar.WriteEntry (Tar.TarEntry.CreateEntryFromFile f.FullName, false))

        tar.Close ()
        stream.Close ()
        File.Move (tmpFilePath, tarFilePath)
        tarFilePath
    | _ ->
        // zip
        let zip = Zip.FastZip ()
//        zip.CompressionLevel <- Zip.Compression.Deflater.CompressionLevel.DEFAULT_COMPRESSION
        if not <| String.IsNullOrEmpty password then zip.Password <- password

        let zipFilePath = outFilePath |> sprintf "%s.zip" |> Path.GetFullPath
        File.Delete zipFilePath
        zip.CreateZip (zipFilePath, Path.GetFullPath srcPath, true, ".+")

        zipFilePath
*)
