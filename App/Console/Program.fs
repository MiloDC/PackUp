module PackUp.Application.Console

open System.IO
open PackUp

let [<Literal>] private DefaultConfigFileName = "packup.json"
let [<Literal>] private FileResultBit = 128
let [<Literal>] private DefaultCaseSens = 0
let [<Literal>] private ProgBarLen = 50
let private AppVersion =
    let assm = System.Reflection.Assembly.GetExecutingAssembly ()
    System.Diagnostics.FileVersionInfo.GetVersionInfo(assm.Location).FileVersion

let private showProgress =
    function
    | Incomplete (config, pct) ->
        (String.replicate (int (single ProgBarLen * pct)) "#").PadRight (ProgBarLen, '_')
        |> printf "\r%s [%s]" config
    | Complete (config, tgtPath) ->
        $"\r{config} -> {tgtPath}".PadRight (config.Length + ProgBarLen + 3, ' ')
        |> printfn "%s"

let private (|ProcessArgs|) (args : string array) =
    args
    |> Array.truncate (FileResultBit - 1)
    |> Array.fold
        (fun ((res, file, configs, caseSens, action), opt) arg ->
            if arg.StartsWith '-' then
                match arg with
                | "-v" -> (res, file, configs, caseSens, List.iter (fun p -> printfn "%O" p)), ""
                | _ -> (res + 1, file, configs, caseSens, action), arg
            else
                match opt with
                | "" ->
                    let f = if File.Exists arg then (FileInfo arg).FullName else ""
                    (res - FileResultBit, f, configs, caseSens, action), "-"
                | "-c" -> (res - 1, file, Set.add (arg.ToLower ()) configs, caseSens, action), ""
                | "-s" -> (res - 1, file, configs, snd (System.Int32.TryParse arg), action), ""
                | _ -> (res + 1, file, configs, caseSens, action), opt)
        ((FileResultBit, "", Set.empty, DefaultCaseSens, List.iter (Pack.pack (Some showProgress)))
            , "")
    |> fst

[<EntryPoint>]
let main (ProcessArgs (result, configsFile, configs, caseSensitivity, action)) =
    let res, cfgFile =
        if (result < FileResultBit) || (not <| File.Exists DefaultConfigFileName) then
            result, configsFile
        else (result - FileResultBit), (FileInfo DefaultConfigFileName).FullName
    if 0 = res then
        match Json.readFile configs caseSensitivity cfgFile with
        | [] -> printfn "ERROR: No packs read, check file syntax."
        | packs -> action packs
    else
        printfn
            $"PackUp version %s{AppVersion}\n\
            Syntax: PackUp [OPTIONS] CONFIG_FILE\n\
            Options:\n    \
                -c CONFIG [-c CONFIG ...] - Pack given configuration(s) only\n    \
                -s BITS - Bitwise case-sensitivity [1 = filenames, 2 = edits] \
                    (default = %d{DefaultCaseSens})\n    \
                -v - view contents of PackUp file only"

    result
