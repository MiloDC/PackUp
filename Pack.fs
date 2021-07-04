namespace PackUp

open System.IO
open System.Text.RegularExpressions

type Pack =
    {
        description     : string
        /// The root of all file paths in files and edits.
        rootDir         : string
        compression     : Compression
        /// Output file name of the compressed file, minus the extension.
        targetPath      : string
        /// Whitelist, blacklist, and include paths, all relative to rootDir.
        files           : Regex list * Regex list * Regex list
        /// File path relative to Pack.rootDir, (Reg. expr. to match, replacement string)
        edits           : (string * (Regex * string) seq) list
        newLine         : NewLine
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
        [ "whitelist", whitelist; "blacklist", blacklist; "includes", includes ]
        |> List.fold
            (fun state (name, reList) ->
                if reList.Length > 0 then
                    if not state then Printf.bprintf sb "files =\n"
                    Printf.bprintf sb $"    %s{name} =\n"
                    reList |> Seq.iter (Printf.bprintf sb "        %O\n")
                    true
                else state)
            false
        |> ignore

        if this.edits.Length > 0 then
            Printf.bprintf sb "edits =\n"
            this.edits
            |> Seq.iter (fun (filePath, reRepls) ->
                Printf.bprintf sb "    \"%s\" =\n" filePath
                reRepls |> Seq.iter (fun (re, repl) ->
                    Printf.bprintf sb "        %O -> \"%s\"\n" re repl))

        string sb

type Progress =
    | Incomplete of config : string * percent : single
    | Complete of config : string * targetPath : string

[<RequireQualifiedAccess>]
module Pack =
    let private reDotSlash = Regex @"^\./"
    let private isRegexMatch str (re : Regex) = re.IsMatch str

    let private invalidPathChars =
        [| '*' |]
        |> Array.append (Path.GetInvalidPathChars ()) |> Array.distinct
    let private validatePath path =
        invalidPathChars
        |> Array.fold (fun (dir : string) c -> dir.Replace (string c, ""))
            (if not <| System.String.IsNullOrWhiteSpace path then path else "_")
        |> fun s -> s.Replace ($"{dirSep}{dirSep}", $"{dirSep}_{dirSep}")

    let pack progressCallback pack =
        let rootDir = $"{normalizePath pack.rootDir}/"
        let mutable packUpRootDir = null
        while isNull packUpRootDir || Directory.Exists packUpRootDir do
            packUpRootDir <-
                sprintf "%s%s%c"
                    (Path.GetTempPath ()) (Path.GetRandomFileName().Replace (".", "")) dirSep
        let newLine = string pack.newLine

        // Copy files.
        let workDir =
            $"{packUpRootDir}{pack.description}{dirSep}{Path.GetFileName pack.targetPath}{dirSep}"
            |> validatePath
        if Directory.Exists workDir then Directory.Delete (workDir, true)
        let whitelist, blacklist, includes = pack.files
        let copyFiles =
            ("*.*", SearchOption.AllDirectories)
            |> DirectoryInfo(Path.GetFullPath pack.rootDir).GetFiles
            |> Array.choose (fun fileInfo ->
                let relativeFilePath = (normalizePath fileInfo.FullName).Replace (rootDir, "./")
                let isMatch = isRegexMatch relativeFilePath
                if
                    (whitelist |> List.exists isMatch)
                    || (    (blacklist |> List.exists isMatch |> not)
                            && (includes |> List.exists isMatch))
                then
                    let relFilePath = reDotSlash.Replace (relativeFilePath, "")
                    Some (
                        Path.GetFullPath $"{rootDir}{relFilePath}",
                        Path.GetFullPath $"{workDir}{relFilePath}")
                else None)
        let jobCount = (Seq.length copyFiles) + 1 |> single   // +1 for compression
        copyFiles
        |> Array.iteri (fun i (srcFile, destFile) ->
            let destDir = Path.GetDirectoryName destFile
            if not <| Directory.Exists destDir then Directory.CreateDirectory destDir |> ignore

            pack.edits
            |> List.tryPick (fun (filePath, editMap) ->
                let fullFilePath =
                    (normalizePath $"{workDir}/{filePath}").Replace ("/./", "/")
                    |> Path.GetFullPath
                if fullFilePath.Equals destFile then Some editMap else None)
            |> Option.bind (fun editMap ->
                use writer = File.CreateText destFile
                writer.NewLine <- newLine
                use reader = File.OpenText srcFile
                while not reader.EndOfStream do
                    editMap
                    |> Seq.fold
                        (fun line (re, repl) -> re.Replace (line, repl))
                        (reader.ReadLine ())
                    |> writer.WriteLine
                Some ())
            |> Option.defaultWith (fun _ -> File.Copy (srcFile, destFile, true))

            progressCallback
            |> Option.iter (fun f -> f (Incomplete (pack.description, single i / jobCount))))

        // Compress files.
        let targetFilePath =
            pack.compression
            |> Compression.compress
                $"{(DirectoryInfo workDir).Parent.FullName}{dirSep}"
                pack.targetPath
        progressCallback |> Option.iter (fun f -> f (Complete (pack.description, targetFilePath)))

        if Directory.Exists packUpRootDir then Directory.Delete (packUpRootDir, true)
