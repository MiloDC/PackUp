namespace PackUp

open System.Text.RegularExpressions

type Pack =
    {
        description     : string
        /// The root of all file paths in files and edits.
        rootDir         : string
        compression     : Compression
        /// Output file name of the compressed file, minus the extension.
        targetPath      : string
        /// Root directory name in the compressed file. If null, empty, or whitespace,
        /// then the root directory name will be the filename component of targetPath.
        targetRoot      : string
        /// Whitelist, blacklist, and include paths, all relative to rootDir.
        files           : Regex list * Regex list * Regex list
        /// File path relative to Pack.rootDir, (Reg. expr. to match, replacement string)
        edits           : (string * (Regex * string) seq) list
        newLine         : NewLine
    }

    override this.ToString () =
        let sb = System.Text.StringBuilder ()

        Printf.bprintf sb $"description = \"%O{this.description}\"\n"

        Printf.bprintf sb $"rootDir = \"%O{this.rootDir}\"\n"

        Printf.bprintf sb "compression = "
        match this.compression with
        | Tar -> Printf.bprintf sb "tar\n"
        | Zip password -> Printf.bprintf sb $"zip (password = \"%O{password}\")\n"
        | TarZip password -> Printf.bprintf sb $"tarzip (password = \"%O{password}\")\n"
        | NoCompression -> Printf.bprintf sb "none\n"

        Printf.bprintf sb $"targetPath = \"%O{this.targetPath}\"\n"

        Printf.bprintf sb $"targetRoot = \"%O{this.targetRoot}\"\n"

        let whitelist, blacklist, includes = this.files
        [ "whitelist", whitelist; "blacklist", blacklist; "includes", includes ]
        |> List.fold
            (fun isFirst (name, reList) ->
                if reList.Length > 0 then
                    if isFirst then Printf.bprintf sb "files =\n"
                    Printf.bprintf sb $"    %s{name} =\n"
                    reList |> Seq.iter (Printf.bprintf sb "        %O\n")
                    false
                else isFirst)
            true
        |> ignore

        if this.edits.Length > 0 then
            Printf.bprintf sb "edits =\n"
            this.edits
            |> Seq.iter (fun (filePath, reRepls) ->
                Printf.bprintf sb $"    \"%O{filePath}\" =\n"
                reRepls |> Seq.iter (fun (re, repl) ->
                    Printf.bprintf sb $"        %O{re} -> \"%O{repl}\"\n"))

        string sb

type Progress =
    | Incomplete of config : string * percent : single
    | Complete of config : string * targetPath : string

[<RequireQualifiedAccess>]
module Pack =
    open System
    open System.IO

    let private reDotSlash = Regex @"^\./"

    let private invalidPathChars =
        [| '*' |]
        |> Array.append (Path.GetInvalidPathChars ()) |> Array.distinct

    let private validatePath path =
        invalidPathChars
        |> Array.fold
            (fun (dir : string) c -> dir.Replace (string c, ""))
            (if not <| System.String.IsNullOrWhiteSpace path then path else "_")
        |> fun s -> s.Replace ($"{dirSep}{dirSep}", $"{dirSep}_{dirSep}")

    let pack progressCallback pack' =
        let p =
            { pack' with
                targetRoot =
                    if not <| String.IsNullOrWhiteSpace pack'.targetRoot then
                        pack'.targetRoot
                    else
                        Path.GetFileName pack'.targetPath
            }
        let rootDir = $"{normalizeFullPath p.rootDir}/"
        let mutable packUpRootDir = ""
        while System.String.IsNullOrEmpty packUpRootDir || Directory.Exists packUpRootDir do
            packUpRootDir <-
                sprintf "%s%s%c"
                    (Path.GetTempPath ())
                    (Path.GetRandomFileName().Replace (".", ""))
                    dirSep
        let newLine = string p.newLine

        // Copy files.
        let workDir =
            $"{packUpRootDir}{p.description}{dirSep}{p.targetRoot}{dirSep}"
            |> validatePath
        if Directory.Exists workDir then Directory.Delete (workDir, true)
        let wl, bl, incl = p.files
        let copyFiles =
            ("*.*", SearchOption.AllDirectories)
            |> DirectoryInfo(Path.GetFullPath p.rootDir).GetFiles
            |> Array.choose (fun fileInfo ->
                let relativeFilePath =
                    (normalizeFullPath fileInfo.FullName).Replace (rootDir, "./")
                let isMatch (re : Regex) = re.IsMatch relativeFilePath
                if
                    (wl |> List.exists isMatch)
                    || ((bl |> List.exists isMatch |> not) && (incl |> List.exists isMatch))
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
            let destFileShort = destFile.Replace (workDir, "") |> normalizePath
            if not <| Directory.Exists destDir then Directory.CreateDirectory destDir |> ignore

            p.edits
            |> List.choose (fun (rePath, editMap) ->
                let rePathShort = rePath.Replace (rootDir, "")
                let re = (normalizePath $"{rePathShort}") |> RE.ofString true false true
                if re.IsMatch destFileShort then Some editMap else None)
            |> (fun editMaps ->
                if List.isEmpty editMaps then None else editMaps |> Seq.concat |> Some)
            |> Option.map (fun editMap ->
                use writer = File.CreateText destFile
                writer.NewLine <- newLine
                use reader = File.OpenText srcFile
                while not reader.EndOfStream do
                    editMap
                    |> Seq.fold
                        (fun line (re, repl) -> re.Replace (line, repl))
                        (reader.ReadLine ())
                    |> writer.WriteLine
                ())
            |> Option.defaultWith (fun _ -> File.Copy (srcFile, destFile, true))

            progressCallback
            |> Option.iter (fun f -> f (Incomplete (p.description, single i / jobCount))))

        // Compress files.
        let targetFilePath =
            p.compression
            |> Compression.compress
                $"{(DirectoryInfo workDir).Parent.FullName}{dirSep}"
                p.targetPath
        progressCallback |> Option.iter (fun f -> f (Complete (p.description, targetFilePath)))

        if Directory.Exists packUpRootDir then Directory.Delete (packUpRootDir, true)
