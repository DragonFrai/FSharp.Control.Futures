[<AutoOpen>]
module Utils

open System.Collections.Generic
open System.Runtime.CompilerServices

let inline ( ^ ) f x = f x

let inline nullObj<'a when 'a : not struct> = Unchecked.defaultof<'a>
let inline isNull<'a when 'a : not struct> (x: 'a) = obj.ReferenceEquals(x, null)
let inline isNotNull<'a when 'a : not struct> (x: 'a) = not (isNull x)

let (|IsNull|IsNotNull|) x = match x with null -> IsNull | _ -> IsNotNull x
let (|IsNullRef|IsNotNullRef|) x = match x with _ when obj.ReferenceEquals(x, null) -> IsNullRef | _ -> IsNotNullRef x

//[<Struct>]
//type OneOrManyList<'a> =
//    [<DefaultValue>]
//    val mutable hasFirst : bool
//    [<DefaultValue>]
//    val mutable first : 'a
//    [<DefaultValue>]
//    val mutable other : List<'a>
//
//
//module OneOrManyList =
//
//    let create () = OneOrManyList<'a>()
//
//    let add (x: 'a) (list: OneOrManyList<'a>) =
//        let mutable list = list
//        if not list.hasFirst then
//            list.first <- x
//            list.hasFirst <- true
//        else
//            if obj.ReferenceEquals(list.other, null) then
//                list.other <- List<'a>()
//                list.other.Add(x)
//            else
//                list.other.Add(x)













