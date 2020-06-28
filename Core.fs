[<AutoOpen>]
module PackUp.Core

open System.Text.RegularExpressions

let [<Literal>] Version = "1.0.0"

let internal dirSep = System.IO.Path.DirectorySeparatorChar

let internal (|NativeFullPath|) = System.IO.Path.GetFullPath

let packUpDirOf (NativeFullPath rootPath) =
    sprintf "%s%c__PACKUP__%c" (rootPath.TrimEnd dirSep) dirSep dirSep

type DirMap = (Regex * Regex seq) list          // (directory, files)
type EditMap = string * (Regex * string) seq    // file_path, (regex, replacement)

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

[<RequireQualifiedAccess>]
module RE =
    let filePath = Regex "(.+/)([^/]+$)"
    let filePathOptions = Regex "^[-]+"

    let regexOf prepString isCaseSensitive (s : string) =
        Regex (
            if prepString then
                (s.Replace(".", "\\.").Replace ("*", ".*")
                |> sprintf "^%s$").Replace (".*/$", ".*$")
            else s
            , if isCaseSensitive then RegexOptions.None else RegexOptions.IgnoreCase)
