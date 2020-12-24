module PackUp.Application.Console

open System.IO
open PackUp

let [<Literal>] private DefaultConfigFileName = "packup.json"
let [<Literal>] private FileResultBit = 128
let [<Literal>] private DefaultCaseSens = 0
let [<Literal>] private progBarLen = 50

let private printProgress = function
    | Incomplete (platform, pct) ->
        (String.replicate (int (single progBarLen * pct)) "#").PadRight (progBarLen, '_')
        |> printf "\r%s [%s]" platform
    | Complete (platform, tgtPath) ->
        $"\r{platform} -> {tgtPath}".PadRight (platform.Length + progBarLen + 3, ' ')
        |> printfn "%s"

let private (|ProcessArgs|) (args : string array) =
    args
    |> Array.truncate (FileResultBit - 1)
    |> Array.fold (fun ((res, file, plats, caseSens, action), opt) arg ->
        if arg.StartsWith '-' then
            match arg with
            | "-v" -> (res, file, plats, caseSens, Seq.iter (fun p -> printfn "%O" p)), ""
            | _ -> (res + 1, file, plats, caseSens, action), arg
        else
            match opt with
            | "" ->
                (res - FileResultBit, (if File.Exists arg then (FileInfo arg).FullName else ""),
                    plats, caseSens, action)
                , "-"
            | "-p" -> (res - 1, file, Set.add (arg.ToLower ()) plats, caseSens, action), ""
            | "-c" -> (res - 1, file, plats, snd (System.Int32.TryParse arg), action), ""
            | _ -> (res + 1, file, plats, caseSens, action), opt)
        ((FileResultBit, "", Set.empty, DefaultCaseSens, Seq.iter (Pack.pack (Some printProgress)))
            , "")
    |> fst

[<EntryPoint>]
let main (ProcessArgs (result, configFile, platforms, caseSensitivity, action)) =
    let res, cfgFile =
        if (result < FileResultBit) || (not <| File.Exists DefaultConfigFileName) then
            result, configFile
        else (result - FileResultBit), (FileInfo DefaultConfigFileName).FullName
    if 0 = res then
        Json.readFile platforms caseSensitivity cfgFile |> action
    else
        printfn "PackUp version %s" Core.Version
        printfn "Syntax: PackUp [OPTIONS] [CONFIG_FILE]"
        printfn "Options:"
        printfn "\t-p PLATFORM [-p PLATFORM ...] - Pack given platform(s) only"
        printf "\t-c # - Bitwise case-sensitivity"
        printfn " [1 = filenames, 2 = edits] (default = %d)" DefaultCaseSens
        printfn "\t-v - output contents of PackUp file only"

    result
