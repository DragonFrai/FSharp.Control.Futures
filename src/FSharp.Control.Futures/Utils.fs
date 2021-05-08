[<AutoOpen>]
module Utils


let inline internal ( ^ ) f x = f x

let inline nullObj<'a when 'a : not struct> = Unchecked.defaultof<'a>
let inline isNull<'a when 'a : not struct> (x: 'a) = obj.ReferenceEquals(x, null)
let inline isNotNull<'a when 'a : not struct> (x: 'a) = not (isNull x)

// let (|IsNull|IsNotNull|) x = match x with null -> IsNull | _ -> IsNotNull x
// let (|IsNullRef|IsNotNullRef|) x = match x with _ when obj.ReferenceEquals(x, null) -> IsNullRef | _ -> IsNotNullRef x



module Data =

    [<AllowNullLiteral>]
    type NullableBox<'a when 'a : struct> =
        val Inner : 'a
        new(inner: 'a) = { Inner = inner }

    type Box<'a when 'a : struct> =
        val Inner : 'a
        new(inner: 'a) = { Inner = inner }

    [<Struct; StructuralEquality; StructuralComparison>]
    type ObjectOption<'a when 'a : not struct> =
        val Value: 'a
        new(value) = { Value = value }

        member inline this.IsNull =
            obj.ReferenceEquals(this.Value, null)

        member inline this.IsNotNull =
            not this.IsNull

    let inline (|ObjectNone|ObjectSome|) (x: ObjectOption<'a>) =
        if x.IsNull then ObjectNone else ObjectSome x.Value


    type [<Struct>]
        InPlaceObjectList<'a when 'a : not struct> =
        val Value: ObjectOption<'a>
        val Tail: ObjectOption<InPlaceObjectListNode<'a>>
    and InPlaceObjectListNode<'a when 'a : not struct> =
        val Value: ObjectOption<'a>
        val Tail: ObjectOption<InPlaceObjectListNode<'a>>


