[<AutoOpen>]
module PackUp.Core

let Version =
    let assm = System.Reflection.Assembly.GetExecutingAssembly ()
    System.Diagnostics.FileVersionInfo.GetVersionInfo(assm.Location).FileVersion

type NewLine =
    | System
    | CR
    | LF
    | CRLF

    static member ofString (str : string) =
        match str.ToUpper () with
        | "CR" -> CR
        | "LF" -> LF
        | "CRLF" -> CRLF
        | _ -> System

    override this.ToString () =
        match this with
        | CR -> "\r"
        | LF -> "\n"
        | CRLF -> "\r\n"
        | System -> System.Environment.NewLine

let internal dirSep = System.IO.Path.DirectorySeparatorChar

let internal (|NativeFullPath|) path = (System.IO.Path.GetFullPath path).TrimEnd dirSep

let normalizePath (path : string) = (path.TrimEnd dirSep).Replace (dirSep, '/')
let normalizeFullPath (NativeFullPath path) = path.Replace (dirSep, '/')

[<RequireQualifiedAccess>]
module RE =
    open System.Text.RegularExpressions

    let private reBeginsDotSlashOrAsterisk = Regex (@"^\./|^\*", RegexOptions.Compiled)

    let ofString prepareString prependDotSlash isCaseSensitive (s : string) =
        Regex (
            (if prepareString then
                (
                    Regex.Escape(s.Replace("?", "<DOT/>").Replace ("*", "<DOT_ASTERISK/>"))
                        .Replace("<DOT_ASTERISK/>", ".*").Replace ("<DOT/>", ".")
                    |> sprintf "^%s%s$"
                        (   if
                                (not prependDotSlash)
                                || (reBeginsDotSlashOrAsterisk.IsMatch s)
                            then "" else @"\./")
                )
                    .Replace (".*/$", ".*$")
            else s)
            , if isCaseSensitive then RegexOptions.None else RegexOptions.IgnoreCase)
