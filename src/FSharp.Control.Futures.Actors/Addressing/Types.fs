namespace FSharp.Control.Futures.Actors.Addressing

open FSharp.Control.Futures

// Этот модуль предоставляет способы отправки сообщений.
// Адресация исходит из следующих предположений:
// - Ожидание успешной отправки сцеплено с ожиданием формирования ответа,
//   потому что ошибка отправки может произойти до самого запихивания сообщения в хендлер актора
// - TBD

[<Interface>]
type IAddress<'m, 'r> =
    abstract Send: message: 'm -> Future<'r>

[<Interface>]
type IDynamicAddress =
    abstract Send<'m, 'r>: message: 'm -> Future<'r>
    abstract Narrow<'m, 'r> : unit -> IAddress<'m, 'r>
