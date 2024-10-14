# Создание Future используя функции-комбинаторы

Используя функции модуля Future можно создать базовые и получить скомбинированные
вариации Future. Разберем базовые функции создания.

```fsharp
// Создает Future, которое моментально завершается с переданным значением
let ready = Future.ready "Hello, world!"

// То же что и `Future.ready ()`, только в единственном экземпляре
let unit' = Future.unit'

// Future, которая никогда не завершается
let never = Future.never<_>

// Future, которое выполнит функцию при своем запуске и вернет её результат.
let lazy' = Future.lazy (fun () -> printfn "Hello, world!")
```

Вышеописанные функции позволяют создать базовые, наиболее простые Future.
Они довольно просты и не проявляют свойств асинхронности, и тем не менее,
могут быть крайне полезны когда вам необходима Future заглушка или
простой способ преобразовать результат или действие в асинхронный примитив.

Все Future можно комбинировать друг с другом используя комбинаторы.
Ключевым является понимание комбинатора Future.bind, который позволяет
передать результат одного асинхронного вычисления по цепочке в следующее.
Рассмотрим его на простом псевдо примере чтения из одного места и записи в другое.

Future.bind имеет сигнатуру `(binder: 'a -> Future<'b>) -> fut: Future<'a> -> Future<'b>` и
создает Future которое передаст результат fut в binder
и дождется результата возвращенного из него Future<'b>.
Можно привести в качестве аналога .then из мира JS.

В примере ниже readAndWriteFuture будет иметь следующее поведение при запуске:
дождется завершения Future, полученным вызовом readFileAsync, которое читает файл "my-file.txt";
затем создаст новое Future записи в файл через writeFileAsync и дождется его завершения.

```fsharp
// readFileAsync: filePath: string -> Future<string>
// writeFileAsync: filePath: string -> content: string -> Future<unit>
let readAndWriteFuture =
    readFileAsync "my-file.txt"
    |> Future.bind (fun content -> writeFileAsync "other-file.txt" content)
```

Также Future.bind могут объединяться в цепочку друг с другом, например так:
```fsharp
let doManyWork =
    doWork1 ()
    |> Future.bind (fun () -> doWork2 ())
    |> Future.bind (fun () -> doWork3 ())
    |> ...
    |> Future.bind (fun () -> doWorkN ())

let doManyWorkWithResults =
    doWork1 ()
    |> Future.bind (fun val1 -> doWork2 val1)
    |> Future.bind (fun val2 -> doWork3 val2)
    |> ...
    |> Future.bind (fun valPrevN -> doWorkN valPrevN)
```

Однако, ситуация сильно усложняется, если единицы работы зависят от результатов друг друга.
```fsharp
let doManyWorkWithCrossResults =
    doWork1 ()
    |> Future.bind (fun val1 ->
        doWork2 val1
        |> Future.bind (fun val2 ->
            doWork3 val1 val2
            |> Future.bind (fun val3 -> ...)))
```

Future.bind позволяет соединять асинхронные вычисления в последовательную цепочку,
и выполнять асинхронную операцию за операцией. Этот процесс можно упростить используя
F# Computation Expressions, о чем будет описано ниже.
Однако перед этим стоит рассмотреть еще несколько комбинаторов.

```fsharp
// Преобразование значения
let map = Future.map (fun n -> n.ToString()) (Future.ready 12)

// Игнорирование значения
let unitFuture = Future.ignore (Future.ready 12)

// Параллельный запуск с ожиданием обоих (ждет 1000 мс)
let merge = Future.merge (Future.sleepMs 1000) (Future.sleepMs 500)

// Параллельный запуск с получением первого выполненного значения и отменой оставшегося
// (Ждет 500 мс)
let first = Future.first (Future.sleepMs 1000) (Future.sleepMs 500)

// Преобразует Future<Future<'a>> в Future<'a>
let join = Future.join (Future.ready (Future.ready 12))

// Ловит исключение вложенной Future, возвращает Result<'a, exn>
let catch = Future.catch (Future.lazy (fun () -> failwith "exception"))
```

<div class="warning">
Future это уже объект машины состояний. По этой причине множественное использование
(комбинирование или запуск) одного экземпляра Future недопустимы.
То есть следующий код, запускающий fut дважды недопустим
```fsharp
let fut = someAsyncWork ()
let doubleFut = fut |> Future.bind (fun () -> fut)
```
Вместо этого всегда пересоздавайте Future при необходимости двойного использования:
```fsharp
let doubleFut = someAsyncWork () |> Future.bind (fun () -> someAsyncWork ())
```
</div>
