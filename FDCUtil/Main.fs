module FDCUtil.Main

type Result<'a, 'b> = 
| Success of 'a
| Failure of 'b
module Result = 
    let mapSuccess f =
        function
        | Success x -> f x |> Success
        | Failure x -> Failure x
    let map = mapSuccess
    let (<?>) = map

    let mapFailure f =
        function
        | Success x -> Success x
        | Failure x -> f x |> Failure

    let bindSuccess m f = 
        match m with
        | Success x -> f x
        | Failure x -> Failure x
    let bind = bindSuccess
    let (>>=) = bind

    let bindFailure m f = 
        match m with
        | Success x -> Success x
        | Failure x -> f x

    let applySuccess mF m =
        match mF with
        | Success f -> mapSuccess f m
        | Failure x -> Failure x
    let apply = applySuccess
    let (<*>) = apply

    let applyFailure mF m = 
        match mF with
        | Success x -> Success x 
        | Failure f -> mapFailure f m

    let fold successF failureF m = 
        match m with
        | Success x -> successF x
        | Failure x -> failureF x

    let getSuccess m = 
        match m with
        | Success x -> 
            x
        | Failure x -> 
            raise (new System.InvalidOperationException(sprintf "Trying to get Success from Failure: %A" x))
    let get = getSuccess

    let getFailure m = 
        match m with
        | Success x -> 
            raise (new System.InvalidOperationException(sprintf "Trying to get Failure from Success: %A" x))
        | Failure x -> 
            x

    type SuccessBuilder() =
        member this.Bind(m, f) = bindSuccess m f
        member this.Return(x) = Success x
        member this.ReturnFrom(x) = x
    let successWorkflow = new SuccessBuilder()

    type FailureBuilder() =
        member this.Bind(m, f) = bindFailure m f
        member this.Return(x) = Failure x
        member this.ReturnFrom(x) = x
    let failureWorkflow = new FailureBuilder()

type ResultList<'a, 'b> = Result<'a, 'b> list
module ResultList =
    let swap (xs: ResultList<'a, _>): Result<'a list, _> =
        let error = 
            xs 
            |> List.tryPick (
                function
                | Success x -> None
                | Failure x -> Failure x |> Some
            ) 
        
        match error with
        | Some x -> x
        | None -> 
            xs
            |> List.choose (
                function
                | Failure x -> None
                | Success x -> x |> Some
            )
            |> Success

    let bind (m: Result<'x list, _>) (f: 'x -> Result<'x list, _>): Result<'x list, _> =
        match m with
        | Success xs ->
            xs
            |> List.map f 
            |> swap 
            |> Result.map List.concat
        | Failure x -> 
            Failure x 
    let (>>=) = bind

module AgentWithComplexState =
    open System.Threading

    type Error = 
    | IsStopped
    | OtherError of System.Exception

    type MessageResult<'a> = Result<'a, Error>
    type FetchResult<'state> = MessageResult<'state>
    type ReplyResult<'state, 'e> = MessageResult<Result<'state, 'e>>

    type Message<'action, 'state, 'e> = 
    | Post of 'action
    | PostAndReply of 'action * AsyncReplyChannel<MessageResult<Result<'state, 'e>>>
    | Fetch of AsyncReplyChannel<MessageResult<'state>>
    | Die

    type T<'action, 'state, 'e> = {
        post: 'action -> unit
        postAndReply: 'action -> ReplyResult<'state, 'e>
        event: IEvent<'state * 'state>
        fetch: unit -> FetchResult<'state>
    }

    let _create (state, deps) f =
        let event = new Event<('state*'deps) * ('state*'deps)>()

        let stopped = new CancellationTokenSource()

        let agent = MailboxProcessor.Start(fun inbox -> 
            let rec loop (accState, accDeps) = async {
                let! agentMessage = inbox.Receive()
                
                match agentMessage with
                | Post x ->
                    let fResult = 
                        try
                            f x (accState, accDeps) |> Success
                        with
                        | ex ->
                            Error.OtherError ex |> Failure
                            
                    match fResult with
                    | Success (Success (accState', accDeps')) ->
                        if (accState' <> accState) then
                            let fullState = (accState, accDeps)
                            let fullState' = (accState', accDeps')
                            event.Trigger((fullState, fullState'))
                        return! loop (accState', accDeps')
                    | _ ->
                        // swallow the error ... nowhere to return it
                        return! loop (accState, accDeps)
                | PostAndReply (x, replyChannel) ->
                    let fResult =          
                        try
                            f x (accState, accDeps) |> Success
                        with
                        | ex ->
                            Error.OtherError ex |> Failure

                    replyChannel.Reply(fResult)

                    match fResult with
                    | Success (Success (accState', accDeps')) ->
                        if (accState' <> accState) then
                            let fullState = (accState, accDeps)
                            let fullState' = (accState', accDeps')
                            event.Trigger((fullState, fullState'))
                        return! loop (accState', accDeps')
                    | _ ->
                        return! loop (accState, accDeps)
                | Fetch replychannel ->
                    replychannel.Reply(Success (accState, accDeps))
                    return! loop (accState, accDeps)
                | Die ->
                    stopped.Cancel()
                    return ()
            }
            loop (state, deps)
        )

        let postAndReplyHO rc = 
            let wait = agent.PostAndAsyncReply(rc)
            let task = Async.StartAsTask(wait, cancellationToken = stopped.Token)
            let res = 
                try
                    Async.AwaitTask task |> Async.RunSynchronously
                with 
                | :? System.OperationCanceledException -> 
                    Error.IsStopped |> Failure
                | ex ->
                    OtherError ex |> Failure
            res

        let post = Post >> agent.Post
        let postAndReply x = postAndReplyHO (fun reply -> PostAndReply (x, reply))
        let fetch () = postAndReplyHO (fun reply -> Fetch reply)
        let stop () = agent.Post Die

        agent,
        stop,
        { 
            post = post
            postAndReply = postAndReply
            fetch = fetch
            event = event.Publish
        }

    let loop fullstate f cb =
        let _agent, stop, agent = _create fullstate f  
        
        let disposable = {
            new System.IDisposable with 
                member x.Dispose() = 
                    stop()
                    (_agent :> System.IDisposable).Dispose()
            } 

        using disposable (fun _ -> cb agent)        

module Regex =

    open System.Text.RegularExpressions

    let (|Regex|_|) pattern input =
        if input = null 
            then None
        else
            let m = Regex.Match(input, pattern)
            if m.Success then Some(List.tail [ for g in m.Groups -> g.Value ])
            else None