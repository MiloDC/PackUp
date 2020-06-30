[<AutoOpen>]
module PackUp.Core

open System.Text.RegularExpressions

let [<Literal>] Version = "1.0.0"

let internal dirSep = System.IO.Path.DirectorySeparatorChar

let internal (|NativeFullPath|) path = (System.IO.Path.GetFullPath path).TrimEnd dirSep

let normalizePath (NativeFullPath path) = path.Replace (dirSep, '/')

[<RequireQualifiedAccess>]
module RE =
    let filePath = Regex "(.+/)([^/]+$)"
    let filePathOptions = Regex "^[-]+"

    let regexOf prepString isCaseSensitive (s : string) =
        Regex (
            if prepString then
                (s.Replace(".", "\\.").Replace("*", ".*").Replace ("?", ".")
                |> sprintf "^%s$").Replace (".*/$", ".*$")
            else s
            , if isCaseSensitive then RegexOptions.None else RegexOptions.IgnoreCase)
