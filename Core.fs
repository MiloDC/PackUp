[<AutoOpen>]
module PackUp.Core

let [<Literal>] Version = "1.2.0"

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

let normalizePath (NativeFullPath path) = path.Replace (dirSep, '/')
