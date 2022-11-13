[<AutoOpen>]
module Utils


let inline internal ( ^ ) f x = f x

let inline refEq (a: obj) (b: obj) = obj.ReferenceEquals(a, b)
let inline nullObj<'a when 'a : not struct> = Unchecked.defaultof<'a>
let inline isNull<'a when 'a : not struct> (x: 'a) = refEq x null
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

[<AllowNullLiteral>]
type IntrusiveNode<'self> when 'self :> IIntrusiveNode<'self>() =
    [<DefaultValue>]
    val mutable next: 'self
    interface IIntrusiveNode<'self> with
        member this.Next
            with get () = this.next
            and set v = this.next <- v

module IntrusiveNode =
    let rec forEach<'a when 'a:> IIntrusiveNode<'a> and 'a: not struct> (f: 'a -> unit) (root: 'a) =
        if isNull root then ()
        else
            f root
            forEach f root.Next

/// Односвязный список, элементы которого являются его же узлами.
/// Может быть полезен для исключения дополнительных аллокаций услов на бодобии услов LinkedList.
/// Например, список ожидающих Context или ожидающих значение 'w: Future
[<Struct>]
type IntrusiveList<'a> when 'a :> IIntrusiveNode<'a> and 'a : not struct =
    val mutable internal startNode: 'a
    val mutable internal endNode: 'a
    new(init: 'a) = { startNode = init; endNode = init }

type IntrusiveList =
    static member Create(): IntrusiveList<'a> = IntrusiveList(nullObj)

    static member Single(x: 'a): IntrusiveList<'a> = IntrusiveList(x)

    static member IsEmpty(list: IntrusiveList<'a> inref): bool =
        isNull list.startNode

    static member PushBack(list: IntrusiveList<'a> byref, x: 'a): unit =
        let mutable list = &list
        if IntrusiveList.IsEmpty(&list) then
            list.startNode <- x
            list.endNode <- x
            x.Next <- nullObj
        else
            list.endNode.Next <- x
            list.endNode <- x

    static member PopFront(list: IntrusiveList<'a> byref): 'a =
        let mutable list = &list
        if IntrusiveList.IsEmpty(&list)
        then nullObj
        elif list.endNode = list.startNode then
            let r = list.startNode
            list.startNode <- nullObj
            list.endNode <- nullObj
            r
        else
            let first = list.startNode
            let second = list.startNode.Next
            list.startNode <- second
            first

    static member Drain(list: IntrusiveList<'a> byref): 'a =
        let mutable list = &list
        let root = list.startNode
        list.startNode <- nullObj
        list.endNode <- nullObj
        root

    static member Remove(list: IntrusiveList<'a> byref, toRemove: 'a): bool =
        let mutable list = &list
        if IntrusiveList.IsEmpty(&list) then
            false
        elif refEq list.startNode toRemove then
            list.startNode <- nullObj
            list.endNode <- nullObj
            true
        elif refEq list.startNode.Next null then
            let rec findParent (childToRemove: obj) (parent: 'a) (child: 'a) =
                if refEq childToRemove child then parent
                elif isNull child.Next then nullObj
                else findParent childToRemove child child.Next
            let parent = findParent toRemove list.startNode list.startNode.Next
            if refEq parent null
            then false
            else
                parent.Next <- parent.Next.Next
                if isNull parent.Next then // ребенок был последней нодой
                    list.endNode <- parent
                true
        else
            false

    static member ToList(list: IntrusiveList<'a> inref): 'a list =
        let root = list.startNode
        let rec collect (c: 'a list) (node: 'a) =
            if isNull node then c
            else collect (c @ [node]) node.Next
        collect [] root

// IntrusiveList
//---------------
