module PackUp.Platform

open Compression

type Platform =
    {
        compression     : Compression
        sourceDir       : string
        targetPath      : string
        files           : DirMap * DirMap       // includes, excludes
        edits           : EditMap list
    }

    member this.bprint sb indentation =
        let indentStr = String.replicate indentation "\t"

        Printf.bprintf sb "%scompression = " indentStr
        match this.compression with
        | Tar -> Printf.bprintf sb "tar\n"
        | Zip password -> Printf.bprintf sb "zip (password = \"%s\")\n" password
        | TarZip password -> Printf.bprintf sb "tarzip (password = \"%s\")\n" password
        | None -> Printf.bprintf sb "none\n"

        Printf.bprintf sb "%ssourceDir = \"%s\"\n" indentStr this.sourceDir

        Printf.bprintf sb "%stargetPath = \"%s\"\n" indentStr this.targetPath

        let includes, excludes = this.files
        if includes.Length > 0 then
            Printf.bprintf sb "%sfiles =\n" indentStr
            bprintDirMap sb (indentation + 1) "includes" includes
        if excludes.Length > 0 then
            if 0 = includes.Length then
                Printf.bprintf sb "%sfiles =\n" indentStr
            bprintDirMap sb (indentation + 1) "excludes" excludes

        if this.edits.Length > 0 then
            Printf.bprintf sb "%sedits =\n" indentStr
            bprintEditMaps sb (indentation + 1) this.edits
