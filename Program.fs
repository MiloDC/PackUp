module PackIt.Program

open System

let private processArgs (args' : string []) =
    args'
    |> Array.fold (fun ((result, (jsonFile, platform, caseSens)), opt) arg ->
        if arg.StartsWith '-' then
            (result + 1, (jsonFile, platform, caseSens)), arg
        else
            match opt with
            | "" -> (result - 1, (arg, platform, caseSens)), "-"
            | "-p" ->
                let a = arg.ToLower ()
                (result - 1, (jsonFile, (if "all" = a then "*" else a), caseSens)), ""
            | "-c" -> (result - 1, (jsonFile, platform, snd (Int32.TryParse arg))), ""
            | _ -> (result + 1, (jsonFile, platform, caseSens)), opt)
        ((1, ("", "*", 0)), "")
    |> fst

[<EntryPoint>]
let main args =
    let processArgsResult, (jsonFile, platform, cs) = processArgs args
    if 0 <> processArgsResult then
        printfn "Syntax: packit [OPTIONS] JSON_FILE"
        printfn "\tOptions:"
        printfn "\t\t-p PLATFORM     Platform to pack (default = all)"
        printf "\t\t-c #            Bitwise case-sensitivity"
        printfn " [1 = directories, 2 = filenames, 4 = edits] (default = 0)"
        1
    else
        let p = readPackItFile platform ((cs &&& 1) > 0) ((cs &&& 2) > 0) ((cs &&& 4) > 0) jsonFile

        printfn "%O" p
        // ...

        0
