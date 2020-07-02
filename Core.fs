[<AutoOpen>]
module PackUp.Core

let [<Literal>] Version = "1.0.0"

let internal dirSep = System.IO.Path.DirectorySeparatorChar

let internal (|NativeFullPath|) path = (System.IO.Path.GetFullPath path).TrimEnd dirSep

let normalizePath (NativeFullPath path) = path.Replace (dirSep, '/')
