namespace FSharp.Control.Futures.LowLevel


[<AllowNullLiteral>]
[<Class>]
type IntrusiveNode<'self> when 'self :> IntrusiveNode<'self>() =
    [<DefaultValue>]
    val mutable next: 'self

module IntrusiveNode =
    /// <summary>
    ///
    /// </summary>
    /// <param name="f"></param>
    /// <param name="root"> Может быть null </param>
    /// <remarks> Может принимать null значение </remarks>
    let inline forEach<'a when 'a:> IntrusiveNode<'a> and 'a: not struct> ([<InlineIfLambda>] f: 'a -> unit) (root: 'a) =
        let mutable node = root
        while isNotNull node do
            f node
            node <- node.next

/// Односвязный список, элементы которого являются его же узлами.
/// Может быть полезен для исключения дополнительных аллокаций услов на подобии услов LinkedList.
/// Например, список ожидающих Context или ожидающих значение 'w: Future
[<Struct>]
type IntrusiveList<'a> when 'a :> IntrusiveNode<'a> and 'a : not struct =
    val mutable internal startNode: 'a
    val mutable internal endNode: 'a
    internal new(init: 'a) = { startNode = init; endNode = init }

type IntrusiveList<'a> when 'a :> IntrusiveNode<'a> and 'a : not struct with
    static member Create(): IntrusiveList<'a> = IntrusiveList(nullObj)

    static member Single(x: 'a): IntrusiveList<'a> = IntrusiveList(x)

    /// Проверяет список на пустоту
    member this.IsEmpty: bool =
        isNull this.startNode

    /// Добавляет элемент в конец
    member this.PushBack(x: 'a): unit =
        if this.IsEmpty then
            this.startNode <- x
            this.endNode <- x
            x.next <- nullObj
        else
            this.endNode.next <- x
            this.endNode <- x

    /// Забирает элемент из начала
    member this.PopFront(): 'a =
        if this.IsEmpty
        then nullObj
        elif refEq this.endNode this.startNode then
            let r = this.startNode
            this.startNode <- nullObj
            this.endNode <- nullObj
            r
        else
            let first = this.startNode
            let second = this.startNode.next
            this.startNode <- second
            first

    /// Опустошает список и возвращает первую ноду, по которой можно проитерироваться.
    /// Может быть полезно для краткосрочного взятия лока на список.
    /// <remarks> Результат может быть null </remarks>
    member this.Drain(): 'a =
        let root = this.startNode
        this.startNode <- nullObj
        this.endNode <- nullObj
        root

    /// Убирает конкретный узел из списка
    member this.Remove(toRemove: 'a): bool =
        if this.IsEmpty then
            false
        elif refEq this.startNode toRemove then
            if refEq this.startNode this.endNode then
                this.startNode <- nullObj
                this.endNode <- nullObj
            else
                this.startNode <- this.startNode.next
            true
        elif isNotNull this.startNode.next then
            let rec findParent (childToRemove: obj) (parent: 'a) (child: 'a) =
                if refEq childToRemove child then parent
                elif isNull child.next then nullObj
                else findParent childToRemove child child.next
            let parent = findParent toRemove this.startNode this.startNode.next
            if refEq parent null
            then false
            else
                parent.next <- parent.next.next
                if isNull parent.next then // ребенок был последней нодой
                    this.endNode <- parent
                true
        else
            false

    member this.ToList(): 'a list =
        let root = this.startNode
        let rec collect (c: 'a list) (node: 'a) =
            if isNull node then c
            else collect (c @ [node]) node.next
        collect [] root
