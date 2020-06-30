[<AutoOpen>]
module PackUp.Core

open System.Text.RegularExpressions

let [<Literal>] Version = "1.0.0"

/// (Reg. expr.  of directory relative to Pack.rootDir, reg. exprs. of file names.)
type DirMap = (Regex * Regex seq) list
/// File path relative to Pack.rootDir, (Reg. expr. to match, replacement string).
type EditMap = string * (Regex * string) seq

let internal dirSep = System.IO.Path.DirectorySeparatorChar

let internal (|NativeFullPath|) path = (System.IO.Path.GetFullPath path).TrimEnd dirSep

let internal bprintDirMap sb indentation name dirMap =
    if List.length dirMap > 0 then
        let indent = String.replicate indentation "\t"
        Printf.bprintf sb "%s%s =\n" indent name
        dirMap
        |> List.iter (fun (dir, files) ->
            Printf.bprintf sb "%s\t%O =\n" indent dir
            files |> Seq.iter (Printf.bprintf sb "%s\t\t%O\n" indent))

let internal bprintEditMaps sb indentation editMaps =
    if Seq.length editMaps > 0 then
        let indent = String.replicate indentation "\t"
        editMaps
        |> Seq.iter (fun (filePath, reRepls) ->
            Printf.bprintf sb "%s\"%s\" =\n" indent filePath
            reRepls |> Seq.iter (fun (re, repl) ->
                Printf.bprintf sb "%s\t%O -> \"%s\"\n" indent re repl))

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
