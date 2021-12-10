[<AutoOpen>]
module Utils


let inline internal ( ^ ) f x = f x

let inline nullObj<'a when 'a : not struct> = Unchecked.defaultof<'a>
let inline isNull<'a when 'a : not struct> (x: 'a) = obj.ReferenceEquals(x, null)
let inline isNotNull<'a when 'a : not struct> (x: 'a) = not (isNull x)
let inline internal ( =&= ) (a: 'a) (b: 'a) = obj.ReferenceEquals(a, b)
let inline internal ( <&> ) (a: 'a) (b: 'a) = not (obj.ReferenceEquals(a, b))

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

type IntrusiveNode<'a> when 'a :> IIntrusiveNode<'a> =
    val mutable next: 'a
    interface IIntrusiveNode<'a> with
        member this.Next
            with get () = this.next
            and set v = this.next <- v

/// Односвязный список, элементы которого являются его же узлами.
/// Может быть полезен для исключения дополнительных аллокаций услов на бодобии услов LinkedList.
/// Например, список ожидающих Context или ожидающих значение 'w: Future
[<Struct>]
type IntrusiveList<'a> when 'a :> IIntrusiveNode<'a> and 'a : not struct =
    val mutable internal startNode: 'a
    val mutable internal endNode: 'a
    new(init: 'a) = { startNode = init; endNode = init }

module IntrusiveList =
    let create () = IntrusiveList(Unchecked.defaultof<'a>)
    let single x = IntrusiveList(x)

    let inline isEmpty (list: inref<IntrusiveList<'a>>) =
        list.startNode = null || list.endNode = null

    let inline isSingle (list: inref<IntrusiveList<'a>>) =
        list.startNode <&> null && list.startNode =&= list.endNode

    let pushBack (x: 'a) (list: byref<IntrusiveList<'a>>) =
        if isEmpty &list then
            list.startNode <- x
            list.endNode <- x
            x.Next <- null
        else
            list.endNode.Next <- x
            list.endNode <- x

    let popFront (list: byref<IntrusiveList<'a>>) =
        if isEmpty &list
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

    /// <summary> Delete element from list and return true on success </summary>
    let remove (toRemove: 'a) (list: byref<IntrusiveList<'a>>): bool =
        let mutable list = &list
        if isEmpty &list then
            false
        elif list.startNode =&= toRemove then
            list.startNode <- null
            list.endNode <- null
            true
        elif list.startNode.Next <&> null then
            let rec findParent (child: 'a) (first: 'a) (second: 'a) =
                if child =&= second then first
                elif second.Next =&= null then findParent child second second.Next
                else null
            let parent = findParent toRemove list.startNode list.startNode.Next
            match parent with
            | null -> false
            | parent ->
                parent.Next <- parent.Next.Next
                true
        else
            false

    let toList (list: inref<IntrusiveList<'a>>) : 'a list =
        let root = list.startNode
        let rec collect (c: 'a list) (node: 'a) =
            if node =&= null then c
            else collect (c @ [node]) node.Next
        collect [] root

// IntrusiveList
//---------------
