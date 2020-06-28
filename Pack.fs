module PackIt.Pack

open System
open System.Collections.Generic
open System.IO
open Platform

type Pack =
    {
        version         : string
        rootDir         : DirectoryInfo
        globalFiles     : DirMap * DirMap       // includes, excludes
        globalEdits     : EditMap list
        platforms       : (string * Platform) seq
    }

    override this.ToString () =
        let sb = System.Text.StringBuilder ()

        Printf.bprintf sb "version = \"%s\"\n" this.version

        Printf.bprintf sb "rootDir = \"%O\"\n" this.rootDir

        let includes, excludes = this.globalFiles
        if includes.Length > 0 then
            Printf.bprintf sb "globalFiles =\n"
            bprintDirMap sb 1 "includes" includes
        if excludes.Length > 0 then
            if 0 = includes.Length then
                Printf.bprintf sb "globalFiles =\n"
            bprintDirMap sb 1 "excludes" excludes

        if this.globalEdits.Length > 0 then
            Printf.bprintf sb "globalEdits =\n"
            bprintEditMaps sb 1 this.globalEdits

        Printf.bprintf sb "platforms =\n"
        this.platforms
        |> Seq.iter (fun (name, platform) ->
            Printf.bprintf sb "\t\"%s\" =\n" name
            platform.bprint sb 2)

        sb.ToString ()

type Progress =
    | Incomplete of platform : string * percent : single
    | Complete of platform : string * targetPath : string

let private normalizePath (path : string) = path.Replace (dirSep, '/')

let pack (progressCallback : (Progress -> unit) option) pack =
    let rootDir = pack.rootDir.FullName |> sprintf "%s/" |> normalizePath
    let srcFiles =
        pack.rootDir.GetFiles ("*.*", SearchOption.AllDirectories)
        |> Array.map (fun fileInfo ->
            let dir = (sprintf "%s/" fileInfo.DirectoryName |> normalizePath).Replace (rootDir, "")
            (if String.IsNullOrEmpty dir then "./" else dir), fileInfo.Name)

    pack.platforms
    |> Seq.iter (fun (platfmName, platform) ->
        // 1) Copy files
        if Directory.Exists platform.sourceDir then Directory.Delete (platform.sourceDir, true)
        let platformDir = Directory.CreateDirectory platform.sourceDir
        let includes, excludes =
            fst pack.globalFiles @ fst platform.files,
            snd pack.globalFiles @ snd platform.files
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
                        (sprintf "%s/%s%s" platformDir.FullName dir fileName) |> Path.GetFullPath
                    )
                else None)
        let copyCount = Seq.length copyFiles |> single
        copyFiles
        |> Array.iteri (fun i (srcFile, destFile) ->
            let destDir = Path.GetDirectoryName destFile
            if not <| Directory.Exists destDir then Directory.CreateDirectory destDir |> ignore
            File.Copy (srcFile, destFile, true)
            progressCallback |> Option.iter (fun fn ->
                fn (Incomplete (platfmName, 0.975f * (single i / copyCount)))))

        // 2) Edit files
        pack.globalEdits @ platform.edits
        |> List.choose (fun (filePath, editMap) ->
            let (NativeFullPath fullFilePath) =
                (sprintf "%s/%s" platformDir.FullName filePath).Replace ("/./", "/")
            if File.Exists fullFilePath then Some (fullFilePath, editMap) else None)
        |> List.iter (fun (filePath, edits) ->
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
        progressCallback |> Option.iter (fun fn -> fn (Incomplete (platfmName, 0.99f)))

        // 3) Compress files
        let targetPath =
            platform.compression
            |> Compression.compress
                (sprintf "%s%c" (DirectoryInfo platform.sourceDir).Parent.FullName dirSep)
                platform.targetPath
        progressCallback |> Option.iter (fun fn -> fn (Complete (platfmName, targetPath))))

    let packItRootDir = packItDirOf rootDir
    if Directory.Exists packItRootDir then Directory.Delete (packItRootDir, true)
