namespace FSharp.Control.Futures.LowLevel


[<Struct>]
type RefOption<'a when 'a : not struct> =
    val Value: 'a
    new(value: 'a) = { Value = value }

    static member inline None: RefOption<'a> = RefOption(nullObj)
    static member inline Some(value: 'a): RefOption<'a> = RefOption(value)

    member inline this.IsNone: bool = isNull this.Value
    member inline this.IsSome: bool = isNotNull this.Value

[<AutoOpen>]
module RefOptionPatterns =

    let inline (|RefSome|RefNone|) (refOption: RefOption<'a>) : Choice<'a, unit> =
        if refOption.IsSome then
            RefSome refOption.Value
        else
            RefNone

    // [<return: Struct>]
    // let (|RefSome|_|) (refOption: RefOption<'a>) : 'a voption =
    //     if refOption.IsSome then
    //         ValueSome refOption.Value
    //     else
    //         ValueNone
    //
    // [<return: Struct>]
    // let (|RefNone|_|) (refOption: RefOption<'a>) : unit voption =
    //     if refOption.IsNone then
    //         ValueSome ()
    //     else
    //         ValueNone
