open System
open PackIt

[<EntryPoint>]
let main args =
    match args with
    | [| jsonFile |] ->
        let p = readPackItFile jsonFile true false
(*
        p.files
        |> Seq.iter (fun (key, incl, excl) ->
            printfn "key = \"%s\"" key
            printfn "\tincludes ="
            incl |> Seq.iter (fun (dir, files) ->
                printfn "\t\t\"%O\" : [" dir
                files |> Seq.iter (printfn "\t\t\t\"%O\"")
                printfn "\t\t]")
            printfn "\texcludes ="
            excl |> Seq.iter (fun (dir, files) ->
                printfn "\t\t\"%O\" : [" dir
                files |> Seq.iter (printfn "\t\t\t\"%O\"")
                printfn "\t\t]"))
*)
        p.edits
        |> Seq.iter (fun (key, edits) ->
            printfn "key = \"%s\"" key
            printfn "\tedits ="
            edits |> Seq.iter (fun (re, repl) -> printfn "\t\t\"%s\" -> \"%O\"" re repl))
        0
    | _ ->
        printfn "Syntax: packit JSON_FILE"
        1
