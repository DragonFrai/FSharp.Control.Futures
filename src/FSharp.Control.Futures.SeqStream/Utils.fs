[<AutoOpen>]
module internal Utils

let inline ( ^ ) f x = f x

let (|IsNull|IsNotNull|) x = match x with null -> IsNull | _ -> IsNotNull x
let (|IsNullRef|IsNotNullRef|) x = match x with _ when obj.ReferenceEquals(x, null) -> IsNullRef | _ -> IsNotNullRef x
