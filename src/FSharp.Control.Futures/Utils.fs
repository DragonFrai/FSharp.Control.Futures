[<AutoOpen>]
module Utils


let inline internal ( ^ ) f x = f x

let inline nullObj<'a when 'a : not struct> = Unchecked.defaultof<'a>
let inline isNull<'a when 'a : not struct> (x: 'a) = obj.ReferenceEquals(x, null)
let inline isNotNull<'a when 'a : not struct> (x: 'a) = not (isNull x)

// let (|IsNull|IsNotNull|) x = match x with null -> IsNull | _ -> IsNotNull x
// let (|IsNullRef|IsNotNullRef|) x = match x with _ when obj.ReferenceEquals(x, null) -> IsNullRef | _ -> IsNotNullRef x

// TODO: Change to std F# 6 InlineIfLambda
[<System.AttributeUsage(System.AttributeTargets.Parameter)>]
type InlineIfLambdaAttribute() =
    inherit System.Attribute()


//---------------
// IntrusiveList

[<AllowNullLiteral>]
type IIntrusiveNode<'a> when 'a :> IIntrusiveNode<'a> =
    abstract Next: 'a with get, set

/// Односвязный список, элементы которого являются его же узлами.
/// Может быть полезен для исключения дополнительных аллокаций услов на бодобии услов LinkedList.
/// Например, список ожидающих Context или ожидающих значение 'w: IAsyncComputation
[<Struct>]
type IntrusiveList<'a> when 'a :> IIntrusiveNode<'a> and 'a : not struct =
    val mutable internal startNode: 'a
    val mutable internal endNode: 'a
    new(init: 'a) = { startNode = init; endNode = init }

module IntrusiveList =
    let create () = IntrusiveList(Unchecked.defaultof<'a>)
    let single x = IntrusiveList(x)

    let isEmpty (list: IntrusiveList<'a>) =
        list.startNode = null || list.endNode = null

    let pushBack (x: 'a) (list: byref<IntrusiveList<'a>>) =
        if isEmpty list then
            list.startNode <- x
            list.endNode <- x
            x.Next <- null
        else
            list.endNode.Next <- x
            list.endNode <- x

    let popFront (list: byref<IntrusiveList<'a>>) =
        if isEmpty list
        then null
        elif list.endNode = list.startNode then
            let r = list.startNode
            list.startNode <- null
            list.endNode <- null
            r
        else
            let first = list.startNode
            let second = list.startNode.Next
            list.startNode <- second
            first

    let toList (list: byref<IntrusiveList<'a>>) : 'a list =
        let root = list.startNode
        let rec collect (c: 'a list) (node: 'a) =
            if node = null then c
            else collect (c @ [node]) node.Next
        collect [] root

// IntrusiveList
//---------------
