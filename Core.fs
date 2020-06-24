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
    | Tar
    | Zip of password : string
    | TarZip of password : string
    | NoCompression

let internal (|JsonString|_|) (jToken : JToken) =
    if (null <> jToken) && (jToken.Type = JTokenType.String) then
        Some (jToken.Value<string> ())
    else None

let internal (|JsonStringArray|) (jToken : JToken) =
    if (null <> jToken) && (jToken.Type = JTokenType.Array) then
        jToken.Children ()
        |> Seq.choose (fun t ->
            if t.Type = JTokenType.String then
                let str = t.Value<string> ()
                if not <| String.IsNullOrEmpty str then Some str else None
            else None)
    else Seq.empty

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

let internal normalizePath (path : string) = path.Replace (dirSep, '/')

let internal regexOf prepString isCaseSensitive (s : string) =
    Regex (
        if prepString then
            (s.Replace(".", "\\.").Replace ("*", ".*") |> sprintf "^%s$").Replace (".*/$", ".*$")
        else s
        , if isCaseSensitive then RegexOptions.None else RegexOptions.IgnoreCase)

let rec internal compress compression outPath srcPath =
    let srcPath' = Path.GetFullPath srcPath
    match compression with
    | Tar ->
        let tarFilePath = outPath |> sprintf "%s.tar.gz" |> Path.GetFullPath
        File.Delete tarFilePath
        use stream = new GZip.GZipOutputStream (File.Create tarFilePath)

        use tar = Tar.TarArchive.CreateOutputTarArchive (stream, Tar.TarBuffer.DefaultBlockFactor)
        tar.RootPath <- srcPath'

        let currDir = Directory.GetCurrentDirectory ()
        Directory.SetCurrentDirectory srcPath

        (DirectoryInfo srcPath').GetFiles ("*", SearchOption.AllDirectories)
        |> Array.iter (fun f ->
            if f.FullName <> tarFilePath then
                tar.WriteEntry (Tar.TarEntry.CreateEntryFromFile f.FullName, false))

        Directory.SetCurrentDirectory currDir
        tarFilePath
    | Zip password ->
        let zip = Zip.FastZip ()
//        zip.CompressionLevel <- Zip.Compression.Deflater.CompressionLevel.DEFAULT_COMPRESSION
        if not <| String.IsNullOrEmpty password then zip.Password <- password

        let zipFilePath = outPath |> sprintf "%s.zip" |> Path.GetFullPath
        File.Delete zipFilePath

        let isDirSrcPath = Directory.Exists srcPath'
        let sourceDirctory, fileFilter =
            (if isDirSrcPath then srcPath' else Path.GetDirectoryName srcPath'),
            if isDirSrcPath then ".+" else Path.GetFileName srcPath'
        zip.CreateZip (zipFilePath, sourceDirctory, isDirSrcPath, fileFilter)

        zipFilePath
    | TarZip password ->
        let tarFilePath = compress Tar outPath srcPath
        let zipFilePath = compress (Zip password) outPath tarFilePath
        File.Delete tarFilePath

        zipFilePath
    | NoCompression -> null
