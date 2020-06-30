module PackUp.Pack

open System.IO
open System.Text.RegularExpressions
open Compression

/// (Reg. expr.  of directory relative to Pack.rootDir, reg. exprs. of file names.)
type DirMap = (Regex * Regex seq) list
/// File path relative to Pack.rootDir, (Reg. expr. to match, replacement string).
type EditMap = string * (Regex * string) seq

let private bprintDirMap sb indentation name dirMap =
    if List.length dirMap > 0 then
        let indent = String.replicate indentation "\t"
        Printf.bprintf sb "%s%s =\n" indent name
        dirMap
        |> List.iter (fun (dir, files) ->
            Printf.bprintf sb "%s\t%O =\n" indent dir
            files |> Seq.iter (Printf.bprintf sb "%s\t\t%O\n" indent))

let private bprintEditMaps sb indentation editMaps =
    if Seq.length editMaps > 0 then
        let indent = String.replicate indentation "\t"
        editMaps
        |> Seq.iter (fun (filePath, reRepls) ->
            Printf.bprintf sb "%s\"%s\" =\n" indent filePath
            reRepls |> Seq.iter (fun (re, repl) ->
                Printf.bprintf sb "%s\t%O -> \"%s\"\n" indent re repl))

type Pack =
    {
        description     : string
        /// The root of all of the relative file paths in files and edits.
        rootDir         : string
        /// Includes, excludes.
        compression     : Compression
        /// Output file name of the compressed file, minus the extension.
        targetPath      : string
        /// Includes, exlcudes.
        files           : DirMap * DirMap
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

        let includes, excludes = this.files
        if includes.Length > 0 then
            Printf.bprintf sb "files =\n"
            bprintDirMap sb 1 "includes" includes
        if excludes.Length > 0 then
            if 0 = includes.Length then
                Printf.bprintf sb "files =\n"
            bprintDirMap sb 1 "excludes" excludes

        if this.edits.Length > 0 then
            Printf.bprintf sb "edits =\n"
            bprintEditMaps sb 1 this.edits

        sb.ToString ()

type Progress =
    | Incomplete of platform : string * percent : single
    | Complete of platform : string * targetPath : string

let pack (progressCallback : (Progress -> unit) option) pack =
    let rootDir = pack.rootDir |> normalizePath |> sprintf "%s/"
    let mutable packUpRootDir = null
    while isNull packUpRootDir || Directory.Exists packUpRootDir do
        packUpRootDir <-
            sprintf "%s%s%c"
                (Path.GetTempPath ()) (Path.GetRandomFileName().Replace (".", "")) dirSep
    let srcFiles =
        DirectoryInfo(Path.GetFullPath pack.rootDir).GetFiles ("*.*", SearchOption.AllDirectories)
        |> Array.map (fun fileInfo ->
            let dir =
                (fileInfo.DirectoryName |> normalizePath |> sprintf "%s/").Replace (rootDir, "")
            (if System.String.IsNullOrEmpty dir then "./" else dir), fileInfo.Name)

    // 1) Copy files
    let platformDir =
        sprintf "%s%s%c%s%c"
            packUpRootDir pack.description dirSep (Path.GetFileName pack.targetPath) dirSep
    if Directory.Exists platformDir then Directory.Delete (platformDir, true)
    Directory.CreateDirectory platformDir |> ignore
    let includes, excludes = pack.files
    let copyFiles =
        srcFiles
        |> Array.filter (fun (dir, fileName) ->
            excludes
            |> List.exists (fun (reDir, reFileNames) ->
                reDir.IsMatch dir && reFileNames |> Seq.exists (fun re -> re.IsMatch fileName))
            |> not)
        |> Array.choose (fun (dir, fileName) ->
            if includes |> List.exists (fun (reDir, reFileNames) ->
                reDir.IsMatch dir && reFileNames |> Seq.exists (fun re -> re.IsMatch fileName))
            then
                Some (
                    (sprintf "%s%s%s" rootDir dir fileName) |> Path.GetFullPath,
                    (sprintf "%s/%s%s" platformDir dir fileName) |> Path.GetFullPath)
            else None)
    let copyCount = Seq.length copyFiles |> single
    copyFiles
    |> Array.iteri (fun i (srcFile, destFile) ->
        let destDir = Path.GetDirectoryName destFile
        if not <| Directory.Exists destDir then Directory.CreateDirectory destDir |> ignore
        File.Copy (srcFile, destFile, true)
        progressCallback |> Option.iter (fun fn ->
            fn (Incomplete (pack.description, 0.975f * (single i / copyCount)))))

    // 2) Edit files
    pack.edits
    |> List.choose (fun (filePath, editMap) ->
        let fullFilePath =
            (sprintf "%s/%s" platformDir filePath |> normalizePath).Replace ("/./", "/")
        if File.Exists fullFilePath then Some (fullFilePath, editMap) else None)
    |> List.iter (fun ((NativeFullPath filePath), edits) ->
        let tmpFilePath = Path.GetTempFileName ()
        let writer = tmpFilePath |> File.CreateText
        let reader = File.OpenText filePath
        while not reader.EndOfStream do
            edits
            |> Seq.fold (fun line (re, repl) -> re.Replace (line, repl)) (reader.ReadLine ())
            |> writer.WriteLine
        reader.Close ()
        writer.Close ()
        File.Move (tmpFilePath, filePath, true))
    progressCallback |> Option.iter (fun fn -> fn (Incomplete (pack.description, 0.99f)))

    // 3) Compress files
    let targetFilePath =
        pack.compression
        |> Compression.compress
            (sprintf "%s%c" (DirectoryInfo platformDir).Parent.FullName dirSep)
            pack.targetPath
    progressCallback |> Option.iter (fun fn -> fn (Complete (pack.description, targetFilePath)))

    if Directory.Exists packUpRootDir then Directory.Delete (packUpRootDir, true)
