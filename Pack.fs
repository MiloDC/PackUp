module PackUp.Pack

open System.IO
open System.Text.RegularExpressions
open Compression

/// File path relative to Pack.rootDir, (Reg. expr. to match, replacement string).
type EditMap = string * (Regex * string) seq

type Pack =
    {
        description     : string
        /// The root of all of the relative file paths in files and edits.
        rootDir         : string
        compression     : Compression
        /// Output file name of the compressed file, minus the extension.
        targetPath      : string
        /// Whitelist, blacklist, and inlcude paths, all relative to rootDir.
        files           : Regex list * Regex list * Regex list
        edits           : EditMap list
    }

    override this.ToString () =
        let sb = System.Text.StringBuilder ()

        Printf.bprintf sb "description = \"%O\"\n" this.description

        Printf.bprintf sb "rootDir = \"%O\"\n" this.rootDir

        Printf.bprintf sb "compression = "
        match this.compression with
        | Tar -> Printf.bprintf sb "tar\n"
        | Zip password -> Printf.bprintf sb "zip (password = \"%s\")\n" password
        | TarZip password -> Printf.bprintf sb "tarzip (password = \"%s\")\n" password
        | NoCompression -> Printf.bprintf sb "none\n"

        Printf.bprintf sb "targetPath = \"%s\"\n" this.targetPath

        let whitelist, blacklist, includes = this.files
        if whitelist.Length > 0 then
            Printf.bprintf sb "files =\n\twhitelist =\n"
            whitelist |> Seq.iter (Printf.bprintf sb "\t\t%O\n")
        if blacklist.Length > 0 then
            if 0 = whitelist.Length then
                Printf.bprintf sb "files =\n"
            Printf.bprintf sb "\tblacklist =\n"
            blacklist |> Seq.iter (Printf.bprintf sb "\t\t%O\n")
        if includes.Length > 0 then
            if 0 = whitelist.Length &&  0 = blacklist.Length then
                Printf.bprintf sb "files =\n"
            Printf.bprintf sb "\tincludes =\n"
            includes |> Seq.iter (Printf.bprintf sb "\t\t%O\n")

        if this.edits.Length > 0 then
            Printf.bprintf sb "edits =\n"
            this.edits
            |> Seq.iter (fun (filePath, reRepls) ->
                Printf.bprintf sb "\t\"%s\" =\n" filePath
                reRepls |> Seq.iter (fun (re, repl) ->
                    Printf.bprintf sb "\t\t%O -> \"%s\"\n" re repl))

        sb.ToString ()

type Progress =
    | Incomplete of platform : string * percent : single
    | Complete of platform : string * targetPath : string

let private reDotSlash = Regex @"^\./"
let private isRegexMatch str (re : Regex) = re.IsMatch str

let private invalidPathChars =
    [| '*' |]
    |> Array.append (Path.GetInvalidPathChars ()) |> Array.distinct
let private validatePath path =
    invalidPathChars
    |> Array.fold (fun (dir : string) c -> dir.Replace (string c, ""))
        (if not <| System.String.IsNullOrWhiteSpace path then path else "_")
    |> fun s -> s.Replace (sprintf "%c%c" dirSep dirSep, sprintf "%c_%c" dirSep dirSep)

let pack progressCallback pack =
    let rootDir = pack.rootDir |> normalizePath |> sprintf "%s/"
    let mutable packUpRootDir = null
    while isNull packUpRootDir || Directory.Exists packUpRootDir do
        packUpRootDir <-
            sprintf "%s%s%c"
                (Path.GetTempPath ()) (Path.GetRandomFileName().Replace (".", "")) dirSep

    // Copy files.
    let platformDir =
        sprintf "%s%s%c%s%c"
            packUpRootDir pack.description dirSep (Path.GetFileName pack.targetPath) dirSep
        |> validatePath
    if Directory.Exists platformDir then Directory.Delete (platformDir, true)
    let whitelist, blacklist, includes = pack.files
    let copyFiles =
        DirectoryInfo(Path.GetFullPath pack.rootDir).GetFiles ("*.*", SearchOption.AllDirectories)
        |> Array.choose (fun fileInfo ->
            let relativeFilePath = (normalizePath fileInfo.FullName).Replace (rootDir, "./")
            let isMatch = isRegexMatch relativeFilePath
            if
                (whitelist |> List.exists isMatch)
                || ((blacklist |> List.exists isMatch |> not) && (includes |> List.exists isMatch))
            then
                let relFilePath = reDotSlash.Replace (relativeFilePath, "")
                Some (
                    (sprintf "%s%s" rootDir relFilePath) |> Path.GetFullPath,
                    (sprintf "%s%s" platformDir relFilePath) |> Path.GetFullPath)
            else None)
    let copyCount = Seq.length copyFiles |> single
    copyFiles
    |> Array.iteri (fun i (srcFile, destFile) ->
        let destDir = Path.GetDirectoryName destFile
        if not <| Directory.Exists destDir then Directory.CreateDirectory destDir |> ignore

        pack.edits
        |> List.tryPick (fun (filePath, editMap) ->
            let fullFilePath =
                (sprintf "%s/%s" platformDir filePath |> normalizePath).Replace ("/./", "/")
                |> Path.GetFullPath
            if fullFilePath.Equals destFile then Some editMap else None)
        |> Option.bind (fun editMap ->
            let writer = File.CreateText destFile
            let reader = File.OpenText srcFile
            while not reader.EndOfStream do
                editMap
                |> Seq.fold (fun line (re, repl) -> re.Replace (line, repl)) (reader.ReadLine ())
                |> writer.WriteLine
            reader.Close ()
            writer.Close () |> Some)
        |> Option.defaultWith (fun _ -> File.Copy (srcFile, destFile, true))

        progressCallback |> Option.iter (fun f ->
            f (Incomplete (pack.description, 0.99f * (single i / copyCount)))))

    // Compress files.
    let targetFilePath =
        pack.compression
        |> Compression.compress
            (sprintf "%s%c" (DirectoryInfo platformDir).Parent.FullName dirSep)
            pack.targetPath
    progressCallback |> Option.iter (fun f -> f (Complete (pack.description, targetFilePath)))

    if Directory.Exists packUpRootDir then Directory.Delete (packUpRootDir, true)
