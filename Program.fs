module PackIt.Program

open System

let [<Literal>] DefaultCaseSensitivty = 0

let private (|ProcessArgs|) (args : string array) =
    let result, jsonFile, platforms, caseSens =
        args
        |> Array.fold (fun ((res, jf, plats, cs), opt) arg ->
            if arg.StartsWith '-' then
                (res + 1, jf, plats, cs), arg
            else
                match opt with
                | "" -> (res - 1, arg, plats, cs), "-"
                | "-p" -> (res - 1, jf, Set.add (arg.ToLower ()) plats, cs), ""
                | "-c" -> (res - 1, jf, plats, snd (Int32.TryParse arg)), ""
                | _ -> (res + 1, jf, plats, cs), opt)
            ((1, "", Set.empty, DefaultCaseSensitivty), "")
        |> fst

    result, (jsonFile, platforms, caseSens)

[<EntryPoint>]
let main = function
    | ProcessArgs (0, (jsonFile, platforms, caseSens)) ->
        jsonFile
        |> Pack.read platforms caseSens
        |> Pack.pack

        0
    | _ ->
        printfn "Syntax: packit [OPTIONS] JSON_FILE"
        printfn "\tOptions:"
        printfn "\t\t-p PLATFORM [-p PLATFORM ...] - Pack given platform(s) only"
        printf "\t\t-c # - Bitwise case-sensitivity"
        printfn " [1 = filenames, 2 = edits] (default = %d)" DefaultCaseSensitivty

        1
