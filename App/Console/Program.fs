module PackIt.Program

open Pack

let [<Literal>] DefaultCaseSensitivty = 0

let private progressBar = function
    | Incomplete (platform, pct) ->
        printf "\r%s [%s]"
            (platform.Substring(0, min platform.Length 7).PadRight (7, ' '))
            ((String.replicate (int (50.f * pct)) "#").PadRight (50, '_'))
    | Complete (platform, outPath) -> printfn "\r%s -> %s" platform outPath

let private (|ProcessArgs|) (args : string array) =
    args
    |> Array.fold (fun ((result, file, plats, caseSens, func), opt) arg ->
        if arg.StartsWith '-' then
            match arg with
            | "-v" -> (result, file, plats, caseSens, printfn "%O"), ""
            | _ -> (result + 1, file, plats, caseSens, func), arg
        else
            match opt with
            | "" -> (result - 1, arg, plats, caseSens, func), "-"
            | "-p" -> (result - 1, file, Set.add (arg.ToLower ()) plats, caseSens, func), ""
            | "-c" -> (result - 1, file, plats, snd (System.Int32.TryParse arg), func), ""
            | _ -> (result + 1, file, plats, caseSens, func), opt)
        ((1, "", Set.empty, DefaultCaseSensitivty, pack (Some progressBar)), "")
    |> fst

[<EntryPoint>]
let main = function
    | ProcessArgs (0, jsonFile, platforms, caseSensitivity, func) ->
        Json.readFile platforms caseSensitivity jsonFile |> func
        0
    | _ ->
        printfn "PackIt version %s" Core.Version
        printfn "Syntax: packit [OPTIONS] JSON_FILE"
        printfn "Options:"
        printfn "\t-p PLATFORM [-p PLATFORM ...] - Pack given platform(s) only"
        printf "\t-c # - Bitwise case-sensitivity"
        printfn " [1 = filenames, 2 = edits] (default = %d)" DefaultCaseSensitivty
        printfn "\t-v - output contents of PackIt file only"
        1
