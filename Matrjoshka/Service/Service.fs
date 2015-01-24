namespace Matrjoshka

open System.Threading

type Service(port : int) =
    let Log = Logging.logger "service"

    let quotes =
                [|
                    "You know nothing, Jon Snow!"
                    "Brace yourself! Winter is coming!"
                    "When you play the game of thrones, you win or you die."
                    "Fear cuts deeper than swords."
                    "What do we say to the Lord of Death? - Not today."
                    "Noseless and Handless, the Lannister Boys."
                    "The North remembers."
                    "A Lannister always pays his debts."
                    "The man who passes the sentence should swing the sword."
                    "A dragon is not a slave."
                |]

    let r = System.Random()

    let pages =
        Map.ofList [
            "/", fun _ ->
                
                let id = r.Next(quotes.Length)
                let q = quotes.[id]
                Log.info "quote: %A" q
                q
        ]

    let s = HttpServer(port, pages)

    member x.Start(?ct : CancellationToken) = 
        Log.info "starting service"
        match ct with 
            | Some ct -> s.Start(ct)
            | _ -> s.Start()

    member x.Run() = s.Run()
