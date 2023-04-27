namespace PackUp

type Compression =
    | Tar
    | Zip of password : string
    | TarZip of password : string
    | NoCompression

[<RequireQualifiedAccess>]
module internal Compression =
    open System.IO
    open ICSharpCode.SharpZipLib

    let rec compress (NativeFullPath srcPath) (NativeFullPath targetPath) compression =
        match compression with
        | Tar ->
            let tarFilePath = $"{targetPath}.tar.gz"
            File.Delete tarFilePath
            use stream = new GZip.GZipOutputStream (File.Create tarFilePath)
            use tar =
                Tar.TarArchive.CreateOutputTarArchive (stream, Tar.TarBuffer.DefaultBlockFactor)
            tar.RootPath <- srcPath

            let d = Directory.GetCurrentDirectory ()
            Directory.SetCurrentDirectory srcPath
            (DirectoryInfo srcPath).GetFiles ("*", SearchOption.AllDirectories)
            |> Array.iter (fun f ->
                if f.FullName <> tarFilePath then
                    tar.WriteEntry (Tar.TarEntry.CreateEntryFromFile f.FullName, false))
            Directory.SetCurrentDirectory d

            tarFilePath
        | Zip password ->
            let isDirSrcPath = Directory.Exists srcPath
            let sourceDirctory = if isDirSrcPath then srcPath else Path.GetDirectoryName srcPath
            let fileFilter = if isDirSrcPath then ".+" else Path.GetFileName srcPath

            let zip = Zip.FastZip ()
//            zip.CompressionLevel <- Zip.Compression.Deflater.CompressionLevel.DEFAULT_COMPRESSION
            if not <| System.String.IsNullOrEmpty password then zip.Password <- password

            let zipFilePath = $"{targetPath}.zip"
            File.Delete zipFilePath
            zip.CreateZip (zipFilePath, sourceDirctory, isDirSrcPath, fileFilter)

            zipFilePath
        | TarZip password ->
            let tarFilePath = compress srcPath targetPath Tar
            let zipFilePath = compress tarFilePath targetPath (Zip password)
            File.Delete tarFilePath

            zipFilePath
        | NoCompression -> null
