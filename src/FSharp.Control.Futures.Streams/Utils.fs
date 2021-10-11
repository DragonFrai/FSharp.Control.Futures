[<AutoOpen>]
module internal Utils

[<RequireQualifiedAccess>]
module Option =
    let ofValueOption = function
        | ValueSome x -> Some x
        | ValueNone -> None
    let toValueOption = function
        | Some x -> ValueSome x
        | None -> ValueNone

let inline ( ^ ) f x = f x

let inline nullObj<'a when 'a : not struct> = Unchecked.defaultof<'a>
let inline isNull<'a when 'a : not struct> (x: 'a) = obj.ReferenceEquals(x, null)
let inline isNotNull<'a when 'a : not struct> (x: 'a) = not (isNull x)

//let (|IsNull|IsNotNull|) x = match x with null -> IsNull | _ -> IsNotNull x
//let (|IsNullRef|IsNotNullRef|) x = match x with _ when obj.ReferenceEquals(x, null) -> IsNullRef | _ -> IsNotNullRef x

let notImplemented fmt = Printf.kprintf (System.NotImplementedException >> raise) fmt
let unimpl fmt = notImplemented fmt
