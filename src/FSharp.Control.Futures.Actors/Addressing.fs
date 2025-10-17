namespace FSharp.Control.Futures.Actors.Addressing

open FSharp.Control.Futures

// Этот модуль предоставляет способы отправки сообщений.
// Адресация исходит из следующих предположений:
// 1. Отправка всегда подразумевает ожидания ответа.
//    - Обоснование: Ожидание позволяет блокировать отправителя пока очередь получателя переполнена.
//      Поддержка лимитированной очереди сообщений и блокировки отправителя крайне важны для обеспечения корректности,
//      т.к. fire-and-forget отправка в неограниченную очередь может приводить к бесконечному накоплению сообщений.
//      Обход всегда возможен, можно запустить много Future отправки подряд, но, по крайней мере,
//      отправка без ожидания может быть исключена из требований к дизайну.
// 2. Отправка сообщений позволяет получить ошибки их обработки в виде исключений.
//    - Прим: (Более высокоуровневый дизайн подразумевает что эти ошибки ближе к уровню инфраструктуры чем логики
//      [неподдерживаемый тип сообщения или ответа, ошибка отправки по сети, актор уже мертв и т.д.],
//      т.к. они не могут быть перечислены явно и построение логики сомнительно, но это личное дело каждого.)
// 3. Address-типы не заботятся о НЕ формировании возвращаемого значения, если нет того кто его ждет.
//    - Обоснование 1: смотреть пункт 1
//    - Обоснование 2: Не формирование ответа актора (например тяжеловесного json представления) может быть обеспечено
//      перегрузкой 'reply типа, а не специальной отправкой с игнорированием.
//


[<Interface>]
type IAddress<'m, 'r> =
    /// <summary> Send the message to target and return Future that awaits return value </summary>
    abstract Send: message: 'm -> Future<'r>

[<Interface>]
type IDynamicAddress =
    abstract Send<'m, 'r>: message: 'm -> Future<'r>


[<RequireQualifiedAccess>]
module internal Addresses =
    [<Class>]
    [<Sealed>]
    type NarrowedAddress<'i, 'o>(target: IDynamicAddress) =
        interface IAddress<'i, 'o> with
            member this.Send(message) = target.Send<'i, 'o>(message)

    [<Class>]
    [<Sealed>]
    type TransformAddress<'i, 'o, 'j, 'u>(address: IAddress<'i, 'o>, mapMsg: 'j -> 'i, mapRep: 'o -> 'u) =
        interface IAddress<'j, 'u> with
            member this.Send(message) = future {
                let message' = mapMsg message
                let! result' = address.Send(message')
                let result = mapRep result'
                return result
            }

    [<Class>]
    [<Sealed>]
    type RouteAddress<'m, 'r>(route: 'm -> IAddress<'m, 'r>) =
        interface IAddress<'m, 'r> with
            member this.Send(message) = future {
                let dest = route message
                return! dest.Send(message)
            }

    [<Class>]
    [<Sealed>]
    type PipeAddress<'m, 'r0, 'r1>(address0: IAddress<'m, 'r0>, address1: IAddress<'r0, 'r1>) =
        interface IAddress<'m, 'r1> with
            member this.Send(message) = future {
                let! result0 = address0.Send(message)
                return! address1.Send(result0)
            }




[<AutoOpen>]
module DynamicAddressNarrowExtension =
    type IDynamicAddress with
        member this.Narrow<'i, 'o>(): IAddress<'i, 'o> = Addresses.NarrowedAddress(this)


[<RequireQualifiedAccess>]
module Address =

    let send (message: 'i) (address: IAddress<'i, 'o>) : Future<'o> =
        address.Send(message)

    let map (mapMsg: 'i -> 'm) (mapRep: 'r -> 'o) (addr: IAddress<'m, 'r>) : IAddress<'i, 'o> =
        Addresses.TransformAddress(addr, mapMsg, mapRep)

    let mapMsg (mapper: 'i -> 'm) (addr: IAddress<'m, 'r>) : IAddress<'i, 'r> =
        Addresses.TransformAddress(addr, mapper, id)

    let mapReply (mapper: 'r -> 'o) (addr: IAddress<'m, 'r>) : IAddress<'m, 'o> =
        Addresses.TransformAddress(addr, id, mapper)


[<RequireQualifiedAccess>]
module AddressEx =

    let pipe (address1: IAddress<'r0, 'r1>) (address0: IAddress<'m, 'r0>) : IAddress<'m, 'r1> =
        Addresses.PipeAddress(address0, address1)

    let route (router: 'm -> IAddress<'m, 'r>) : IAddress<'m, 'r> =
        Addresses.RouteAddress(router)


[<RequireQualifiedAccess>]
module DynamicAddress =

    let send<'i, 'o> (message: 'i) (address: IDynamicAddress) : Future<'o> =
        address.Send(message)

    let narrow<'i, 'o> (address: IDynamicAddress) : IAddress<'i, 'o> =
        address.Narrow()
