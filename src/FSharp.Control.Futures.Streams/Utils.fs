[<AutoOpen>]
module internal Utils

open FSharp.Control.Futures


let inline ( ^ ) f x = f x
//
//let (|IsNull|IsNotNull|) x = match x with null -> IsNull | _ -> IsNotNull x
//let (|IsNullRef|IsNotNullRef|) x = match x with _ when obj.ReferenceEquals(x, null) -> IsNullRef | _ -> IsNotNullRef x

let notImplemented fmt = Printf.kprintf (System.NotImplementedException >> raise) fmt
let unimpl fmt = notImplemented fmt
