module PackUp.Application.Console

open System.IO
open PackUp
open PackUp.Pack

let [<Literal>] private DefaultCaseSensitivty = 0
let [<Literal>] private progBarLen = 50

let private progressBar = function
    | Incomplete (platform, pct) ->
        (String.replicate (int (single progBarLen * pct)) "#").PadRight (progBarLen, '_')
        |> printf "\r%s [%s]" platform
    | Complete (platform, tgtPath) ->
        (sprintf "\r%s -> %s" platform tgtPath).PadRight (platform.Length + progBarLen + 3, ' ')
        |> printfn "%s"

let private (|ProcessArgs|) (args : string array) =
    args
    |> Array.fold (fun ((result, file, plats, caseSens, action), opt) arg ->
        if arg.StartsWith '-' then
            match arg with
            | "-v" -> (result, file, plats, caseSens, Seq.iter (fun p -> printfn "%O" p)), ""
            | _ -> (result + 1, file, plats, caseSens, action), arg
        else
            match opt with
            | "" when File.Exists arg ->
                (result - 1, (FileInfo arg).FullName, plats, caseSens, action), "-"
            | "-p" -> (result - 1, file, Set.add (arg.ToLower ()) plats, caseSens, action), ""
            | "-c" -> (result - 1, file, plats, snd (System.Int32.TryParse arg), action), ""
            | _ -> (result + 1, file, plats, caseSens, action), opt)
        ((1, "", Set.empty, DefaultCaseSensitivty, Seq.iter (pack (Some progressBar))), "")
    |> fst

[<EntryPoint>]
let main (ProcessArgs (result, jsonFile, platforms, caseSensitivity, action)) =
    if 0 = result then
        Json.readFile platforms caseSensitivity jsonFile |> action
    else
        printfn "PackUp version %s" Core.Version
        printfn "Syntax: PackUp [OPTIONS] JSON_FILE"
        printfn "Options:"
        printfn "\t-p PLATFORM [-p PLATFORM ...] - Pack given platform(s) only"
        printf "\t-c # - Bitwise case-sensitivity"
        printfn " [1 = filenames, 2 = edits] (default = %d)" DefaultCaseSensitivty
        printfn "\t-v - output contents of PackUp file only"

    result
