[<AutoOpen>]
module Utils

let inline ( ^ ) f x = f x



//[<Struct>]
//type OnceCell<'a> = private { mutable Value: 'a option }
//
//module OnceCell =
//    
//    let create () : OnceCell<'a> = { Value = None }
//    
//    let getOrPutWith init (once: OnceCell<'a>) =
//        match once.Value with
//        | None ->
//            let mutable once = once
//            let initial = init ()
//            once.Value <- Some initial
//            initial
//        | Some v -> v
//    
//    let getOrPut init (once: OnceCell<'a>) =
//        match once.Value with
//        | None ->
//            let mutable once = once
//            let initial = init
//            once.Value <- Some initial
//            initial
//        | Some v -> v
//    
//    let isEmpty (cell: OnceCell<'a>) = cell.Value.IsNone
//    
//    let isFilled (cell: OnceCell<'a>) = cell.Value.IsSome
//    
//    let value (cell: OnceCell<'a>) = cell.Value