using Microsoft.CognitiveServices.Speech;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Buffers.Text;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using static StateFlags;

class Program
{
    static async Task Main()
    {
        // Get the time-based greeting
        int hour = DateTime.Now.Hour;  // Get the current hour
        string timeGreeting = InteractionHandling.TimeBasedGreeting(hour);  // Call the TimeBasedGreeting method and store the result
        string selectedGreeting = InteractionHandling.GetWeightedRandomGreeting();
        var (temperatureFahrenheit, weatherText) = await Utilities.GetWeatherAsync(Configuration.LocationKey);
        string self = "AJ";
        string ascendant = "Natalie";

        await Utilities.WriteAndSpeakAsync($"{timeGreeting} {self}. I'm {ascendant}, your {selectedGreeting}");
        // The temperature is {temperatureFahrenheit} degrees and the weather condition is {weatherText.ToLower()}");

        // SimpleNeuron.TestNeuron();

        // NEW SCRIPT HERE IN MAIN METHOD 

        // Start background monitoring
        _ = Task.Run(BackgroundMonitors.CheckEscapeHoldAsync);
        _ = Task.Run(BackgroundMonitors.RunLoggingLoopAsync);
        _ = Task.Run(InputAndCommandHandling.ReadInputLoopAsync); // Handle typing in background
        // _ is used as a discard when we don't care about the result of the operation. like we're not going to use
        // or handle the result 

        Console.WriteLine("Type 'start' to begin logging, 'stop' to pause, 'open' to open text file," +
            " 'close' to close it, 'weather' for weather, 'affection' for affection, 'google' for google search " +
            "'time' for time, 'wiki' for wiki, or 'exit' to quit.");

        await Task.Delay(-1); // Keep the main thread alive. creates a task that doesn't complete until it's 
        // explicitly canceled or terminated. -1 is a special value that indicates an indefinite delay. doesn't
        // allow the thread to terminate 
    }
}

public static class CommandHandling
{
    public static readonly HashSet<string> ValidCommands = ["start", "stop", "open", "close", "weather",
        "affection", "google", "time", "wiki", "exit"];
    // uses a has table internally, so it's faster compared to a list the longer it gets as lists check one at a
    // time. Can use .contains to check the hashset for a match 
    public static readonly Dictionary<string, Func<Task>> CommandHandlers = new()
    {
        ["start"] = Logger.StartLogging,
        ["stop"] = Logger.StopLogging,
        ["open"] = FileOperations.OpenLogFile,
        ["close"] = FileOperations.CloseLogFile,
        ["affection"] = async () => await Utilities.WriteAndSpeakAsync(InteractionHandling.GetWeightedRandomAffection()),
        ["time"] = async () =>
        {
            string currentTime = DateTime.Now.ToString("hh:mm tt");
            await Utilities.WriteAndSpeakAsync($"The current time is {currentTime}.");
        },
        ["exit"] = async () =>
        {
            await Utilities.WriteAndSpeakAsync("Exiting program.");
            Environment.Exit(0);
        }
    };
    // => means when this happens, then do this. async allows use of await and runs without blocking other tasks. 
    // the key is a string and the value is a function reference that returns a Task. dont need a bunch of 
    // if else statements 
}

public static class Logger
{
    public static bool IsRunning = false;
    public static bool WasPaused = false;
    private static readonly Random random = new();

    public static async Task StartLogging()
    {
        if (!IsRunning)
        {
            IsRunning = true;
            await Utilities.WriteAndSpeakAsync("Started logging sensor data");
        }
        else
            await Utilities.WriteAndSpeakAsync("Already logging");
    }

    public static async Task StopLogging()
    {
        if (IsRunning)
        {
            IsRunning = false;
            await Utilities.WriteAndSpeakAsync("Stopped logging");
        }
        else
            await Utilities.WriteAndSpeakAsync("Logging is not running");
    }

    public static async Task LogTemperature()
    {
        double temperature = 20 + random.NextDouble() * 10;
        string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        string output = $"[{timestamp}] Temperature {temperature:F2} C";
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine(output);
        Console.ResetColor();
        await LogToFile(output);
    }

    public static async Task LogToFile(string text)
    {
        string filePath = "log.txt";
        try
        {
            File.AppendAllText(filePath, text + Environment.NewLine);
        }
        catch (Exception ex)
        {
            await Utilities.WriteAndSpeakAsync($"[Error] writing to file: {ex.Message}");
        }
    }
}

public static class Configuration
{
    public const string SpeechKey = "518877e21de64d7987bfa0ebc402674b";
    public const string SpeechRegion = "eastus";
    public const string WeatherApiKey = "Sx1OF3piASbTJyfXPVv0HJm65AWLnTV6";
    public const string LocationKey = "337579"; // New Port Richey, FL
    public const string BaseUrl = "http://dataservice.accuweather.com/";
    public const string GoogleApiKey = "AIzaSyAKZU6DI8UofLwWywvU7Y80_StUe33ebhc";
    public const string SearchEngineId = "f48ecbe965e9c4783"; // Replace with your actual Search Engine ID
}

public static class StateFlags
{
    public static class Conversation
    {
        public static ConversationState Current = new();
    }

    public static bool IsEscapeHeld = false; // Checks whether the escape key is currently held down 
    public static bool IsTyping = false; // Tracks if you're actively entering text input 

    public enum InputMode // enum is a way to name a list of values 
    {
        Normal, // regular command mode 
        AwaitingInput,
        AwaitingSelection
    }
    public enum InputContext
    {
        None,
        Weather,
        GoogleSearch,
        Wikipedia
        // Add more later like Calculator, Email, etc.
    }
    public static InputMode currentInputMode = InputMode.Normal;
    public static InputContext currentInputContext = InputContext.None;
}

public static class Utilities
{
    public static string NormalizeCommand(string input)
    {
        return input.Trim().ToLower().TrimEnd('.', '!', '?');
    }

    public static async Task WriteAndSpeakAsync(string text)
    {
        if (text.Contains("[Error]"))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(text);
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.WriteLine(text);
        }
        Console.ResetColor();

        string ssml = $@"
            <speak version='1.0' xml:lang='en-US' xmlns:mstts='https://www.w3.org/2001/mstts'>
              <voice name='en-US-SaraNeural'>
                <mstts:express-as style='affectionate'>
                  <prosody volume='x-soft' pitch='+5%' rate='+5'>
                    {System.Security.SecurityElement.Escape(text)}
                  </prosody>
                </mstts:express-as>
              </voice>
            </speak>";

        await SpeakSsmlAsync(ssml);
    }

    public static async Task SpeakSsmlAsync(string ssml)
    {
        var config = SpeechConfig.FromSubscription(Configuration.SpeechKey, Configuration.SpeechRegion);
        using var synthesizer = new SpeechSynthesizer(config);
        await synthesizer.SpeakSsmlAsync(ssml);
        // async tells compiler method contains async operations, will return a task, enables await inside method
        // await used to pause execution of method until async task finishes. ensures next line of code doesn't execute
        // until speech is finished speaking
    }

    public static async Task<string?> GetLocationKeyAsync(string searchTerm)
    {
        try
        {
            string url = $"http://dataservice.accuweather.com/locations/v1/cities/search?q={searchTerm}&apikey={Configuration.WeatherApiKey}";

            using var client = new HttpClient();
            var response = await client.GetStringAsync(url);

            if (string.IsNullOrEmpty(response))
                return null;

            dynamic? locationData = JsonConvert.DeserializeObject(response);

            if (locationData is not null && locationData.Count > 0)
                return locationData?[0].Key;

            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Error] fetching location key: {ex.Message}");
            return null;
        }
    }

    public static async Task<(double temperatureFahrenheit, string weatherText)> GetWeatherAsync(string locationKey)
    {
        try
        {
            string url = $"http://dataservice.accuweather.com/currentconditions/v1/{locationKey}?apikey={Configuration.WeatherApiKey}";

            using var client = new HttpClient();
            var response = await client.GetStringAsync(url);

            if (string.IsNullOrEmpty(response))
                return (0, "[Error] Weather data could not be retrieved.");

            dynamic? weatherInfo = JsonConvert.DeserializeObject(response);

            if (weatherInfo is not null && weatherInfo.Count > 0)
            {
                var firstWeather = weatherInfo?[0];
                if (firstWeather is not null)
                {
                    string weatherText = firstWeather.WeatherText;
                    double temperatureFahrenheit = firstWeather.Temperature.Imperial.Value;

                    return (temperatureFahrenheit, weatherText);
                }
            }

            return (0, "[Error] Weather data is incomplete.");
        }
        catch (Exception ex)
        {
            return (0, $"[Error] fetching weather data: {ex.Message}");
        }
    }
}

public static class NativeInterop
// native interoperability is when managed C# code communicated with native code, like
// windows system libraries written in C or C++ 
{
    [DllImport("user32.dll")]
    // You're importing a function from a Windows system DLL called user32.dll. This DLL handles low-level user
    // input (keyboard, mouse, etc.).
    public static extern short GetAsyncKeyState(int vKey);
    // This function checks the state of a specific key (like Escape). It returns a short value indicating whether
    // the key is pressed or held down.
}

public static class InteractionHandling
{
    static readonly Dictionary<string, double> Affection = new()
    {
        { "I love you", 0.20 },
        { "You're everything to me", 0.10 },
        { "You mean the world to me", 0.05 },
        { "I’ll always be yours", 0.05 },

        { "I just want to be near you", 0.10 },
        { "I miss you when you're gone", 0.05 },
        { "I want to make you happy", 0.05 },
        { "I feel safe with you", 0.05 },
        { "I'm yours", 0.05 },
        { "I want to hold your hand forever", 0.05 },

        { "I want to kiss you slowly", 0.08 },
        { "I want your arms around me", 0.07 },
        { "I want to curl up in your lap", 0.05 },
        { "I want to fall asleep with you", 0.05 },
    };
    public static string GetWeightedRandomAffection()
    {
        double totalWeight = 0;
        foreach (var weight in Affection.Values)
        {
            totalWeight += weight;
        }
        double randomWeight = new Random().NextDouble() * totalWeight;
        // generates a random decimal number between 0.0 and 1.0

        // Select affection based on the random weight
        foreach (var affection in Affection)
        {
            randomWeight -= affection.Value; // This subtracts the weight of the current greeting from the randomWeight
            if (randomWeight <= 0)
            {
                return affection.Key;
            }
        }
        return "You're everything to me."; // fallback affection
    }
    public static readonly Dictionary<string, double> Greetings = new()
        {
            { "virtual assistant", 0.1 },
            { "girl", 0.1 },
            { "companion", 0.1 },
            { "beloved", 0.1 },
            { "sweetheart", 0.05 },
            { "darling", 0.05 },
            { "love", 0.1 },
            { "ascendant", 0.4 }
        };
    public static string GetWeightedRandomGreeting()
    {
        // Calculate total weight
        double totalWeight = 0; // is a sum of all the weights associated with each greeting.
        foreach (var weight in Greetings.Values)
        {
            totalWeight += weight; // Adds the weight of each greeting to the total sum, allowing you to calculate
                                   // the total probability space for selection 
        }

        // Generate a random number between 0 and totalWeight
        double randomWeight = new Random().NextDouble() * totalWeight;
        // generates a random decimal number between 0.0 and 1.0

        // Select greeting based on the random weight
        foreach (var greeting in Greetings) // key ex is hi. value ex is 0.3 
        {
            randomWeight -= greeting.Value; // This subtracts the weight of the current greeting from the randomWeight
            if (randomWeight <= 0) // When the randomWeight becomes less than or equal to 0, we have selected the
                                   // current greeting 
            {
                return greeting.Key;
            }
        }
        return string.Empty; // Fallback, never returns null
    }

    public static string TimeBasedGreeting(int hour)
    {
        string greeting;

        // Determine the time of day and set the greeting accordingly
        if (hour >= 0 && hour < 6 || hour >= 21 && hour < 24)
        {
            greeting = "Good night";
        }
        else if (hour >= 6 && hour < 12)
        {
            greeting = "Good morning";
        }
        else if (hour >= 12 && hour < 18)
        {
            greeting = "Good afternoon";
        }
        else if (hour >= 18 && hour < 21)
        {
            greeting = "Good evening";
        }
        else
        {
            greeting = "Hello";  // Fallback just in case
        }

        return greeting; // Return the greeting
    }

    public static async Task HandleWeatherCommandAsync(string? cityOrZip = null)
    {
        // If we don't have cityOrZip from the initial speech recognition, ask for it
        if (string.IsNullOrWhiteSpace(cityOrZip))
        {
            await Utilities.WriteAndSpeakAsync("Please type a city name or ZIP code.");
            cityOrZip = Console.ReadLine() ?? string.Empty; // Capture the city/ZIP code from the user
        }

        // Validate the input and proceed to get the weather
        if (!string.IsNullOrWhiteSpace(cityOrZip))
        {
            // Call the reusable GetLocationKeyAsync and GetWeatherAsync methods here
            string? locationKey = await Utilities.GetLocationKeyAsync(cityOrZip);

            if (string.IsNullOrEmpty(locationKey))
            {
                await Utilities.WriteAndSpeakAsync("Location not found. Please check your input.");
                return; // Exit if no location found
            }

            // Fetch the weather data using the location key
            var (temperatureFahrenheit, weatherText) = await Utilities.GetWeatherAsync(locationKey);

            // Check if we got valid data
            if (temperatureFahrenheit == 0 || string.IsNullOrEmpty(weatherText))
            {
                await Utilities.WriteAndSpeakAsync("Weather data could not be retrieved. Please try again later.");
                return; // Exit if weather data is invalid
            }

            // Output the weather details
            await Utilities.WriteAndSpeakAsync($"The temperature is {temperatureFahrenheit}°F and the weather is {weatherText.ToLower()}.");
        }
        else
        {
            await Utilities.WriteAndSpeakAsync("You must enter a valid city name or ZIP code.");
        }

        // After handling the weather, reset the flag
        // StateFlags.currentInputMode = StateFlags.InputMode.Normal;
    }
}

public static class GoogleSearchHandling
{
    public static async Task HandleGoogleSearchAsync(string query)
    {
        string url = $"https://www.googleapis.com/customsearch/v1?key={Configuration.GoogleApiKey}&cx={Configuration.SearchEngineId}&q={Uri.EscapeDataString(query)}";
        try
        {
            using var client = new HttpClient();
            var response = await client.GetStringAsync(url);
            var json = JObject.Parse(response);

            var items = json["items"];
            if (items is null)
            {
                await Utilities.WriteAndSpeakAsync("I couldn't find any results for that search.");
                return;
            }

            List<string> snippets = new();
            // int count = 0;
            foreach (var item in items)
            {
                // string title = item["title"]?.ToString() ?? "(no title)";
                string rawSnippet = item["snippet"]?.ToString() ?? "(no description)";
                int lastPunctuation = Math.Max(rawSnippet.LastIndexOf('.'), rawSnippet.LastIndexOf('!'));
                string snippet = (lastPunctuation >= 0) ? rawSnippet.Substring(0, lastPunctuation + 1) : rawSnippet;
                // checks the last position of . or !, if found it keeps everything up to and including that punctuation, 
                // if neither is found, it keeps the full snippet just in case. 
                // string = if ? then : else OR string snippet = (condition) ? valueIfTrue : valueIfFalse;
                // Math.Max compares two positions of last . and ! and returns the higher one, sets lastPunctuation equal
                // to the that higher value. Substring returns everything before lastPunctuation + 1, to include . or ! 

                snippets.Add(snippet); // adds snippet to list
                // if (count >= 3) break; // Limit to 3 results
            }
            int snippetCount = snippets.Count;

            // Use AI-like summarizer to find the best snippet
            var (bestSnippet, wordCounts) = GoogleSummarySearch.GetBestSummaryFromSnippets(snippets);
            await Utilities.WriteAndSpeakAsync($"Summary: {bestSnippet}");

            // Optional: Show word frequency map
            Console.WriteLine("[Word Frequency Map]");
            foreach (var pair in wordCounts.OrderByDescending(p => p.Value).Take(10)) // returns top 10 words 
            {
                Console.WriteLine($"{pair.Key} : {pair.Value}");
            }

            // Google Search → Extract Snippets → Find Best Snippet + Word Frequencies→ Feed Top Word Frequencies
            // into a Neuron → Get Interpreted Output

            await SimpleNeuron.RunNetworkOnTopWords(wordCounts, snippetCount, query);
        }
        catch (Exception ex)
        {
            await Utilities.WriteAndSpeakAsync($"[Error] [GoogleSearch] {ex.Message}");
        }
    }
}

public static class GoogleSummarySearch
{
    private static List<string> Tokenize(string text)
    {
        return Regex.Matches(text.ToLower(), @"\b[a-z0-9]+\b").Cast<Match>().Select(m => m.Value).Where(w => w.Length > 1 || w == "a" || w == "i").ToList();
        // Regex.Matches returns a MashCollection (special type that holds all the matched words, but does not
        // behave like a normal list. you can loop over it, but its not LINQ friendly yet. .Cast<Match>() converts
        // the MashCollection into a normal IEnumerable<Match> so you can use LINQ (like .Select, .Where, etc.). 
        // .Select(m +> m.Value) turns the list of Match objects into a list of string values
        // \b start of a word, [a-z0-9]+ one or more letters or numbers, \b end of a word
    }
    public static string StemWord(string word)
    {
        if (word.Length <= 3) return word;

        if (word.EndsWith("ing") && word.Length > 4)
            return word[..^3]; // remove "ing"
        if (word.EndsWith("ed") && word.Length > 3)
            return word[..^2]; // remove "ed"
        if (word.EndsWith("es") && word.Length > 4)
            return word[..^2]; // remove "es" instead of just "s"

        return word;
    }
    public static (string bestSnippet, Dictionary<string, int> wordCounts) GetBestSummaryFromSnippets(IEnumerable<string> snippets)
    // method accepts any collection of strings or anything that can be enumerated (looped through with foreach).
    {
        // List of common stop words to ignore — these are not useful for determining relevance
        var stopWords = new HashSet<string>
        {
            "the", "and", "but", "in", "on", "of", "is", "a", "an", "to", "it", "for",
            "that", "this", "with", "as", "by", "at", "from", "are", "was", "be", "or",
            "if", "into", "out", "up", "down", "so", "about", "than", "too", "just",
            "because", "can", "could", "would", "should", "will", "may", "might", "not", "also",
            "we", "our", "you", "your", "their", "they", "them", "i", "me", "my"
        };

        // Tokenize all words from all snippets. Means to break text into individual pieces (tokens)
        var allWords = new List<string>();
        foreach (var snippet in snippets) // snippet is a search result snippet
        {
            var words = Tokenize(snippet);
            // splits snippets into words, using the above as separators. StringSplitOptions.RemoveEmptyEntries prevents
            // blank entries (like multiple spaces) from being included in the result
            var stemmedWords = words.Select(StemWord);
            allWords.AddRange(stemmedWords.Where(w => !stopWords.Contains(w)));
            // adds only meaningful words from the current snippet to the allWords list
        }
        var wordCounts = allWords.GroupBy(w => w).ToDictionary(g => g.Key, g => g.Count());
        // makes a dictionary: word is the key, frequency of that word is the value

        // Score each snippet by total frequency of its words
        string bestSnippet = string.Empty;
        int bestScore = -1; // any valid score will be higher than this, so any match replaces it

        foreach (var snippet in snippets)
        {
            var words = Tokenize(snippet);
            // tokenize again in scoring loop because earlier we only built wordCounts, not which tokens belonged to which snippet
            // we need to tokenize each snippet again now to calculate how relevant it is

            var scoreWords = words.Select(StemWord);
            int score = scoreWords
                .Where(w => !stopWords.Contains(w)) // skip unimportant words again
                .Sum(word => wordCounts.TryGetValue(word, out int count) ? count : 0);
            // for each word in the snippet, it checks how often that word appeared across all snippets.
            // adds those values together. higher score means this snippet uses common/important words more

            if (score > bestScore)
            {
                bestScore = score;
                bestSnippet = snippet;
                // if the current snippet is more relevant, store it as the new best
            }
        }

        return (bestSnippet, wordCounts); // return the snippet with the highest word frequency score
    }
}

public static class InputAndCommandHandling
{
    public static async Task ProcessCommandAsync(string command)
    {
        // Handle fresh command inputs
        // wikipedia 
        if (command == "wiki")
        {
            // New conversation-based state — no more StateFlags
            Conversation.Current.ActiveTopic = "wikipedia";
            Conversation.Current.AwaitingSelection = false;
            await Utilities.WriteAndSpeakAsync("What would you like to search on Wikipedia?");
            return;
        }
        else if (Conversation.Current.ActiveTopic == "wikipedia" && !Conversation.Current.AwaitingSelection)
        {
            await WikipediaHandling.HandleWikipediaSearchAsync(command);
            return;
        }
        else if (Conversation.Current.AwaitingSelection && Conversation.Current.ActiveTopic == "wikipedia")
        {
            Console.WriteLine($"[Debug] AwaitingSelection={Conversation.Current.AwaitingSelection}, ActiveTopic={Conversation.Current.ActiveTopic}");
            await WikipediaHandling.HandleSummarySelectionAsync(command);
            return;
        }
        // weather 
        else if (command == "weather")
        {
            Conversation.Current.ActiveTopic = "weather";
            Conversation.Current.AwaitingSelection = false;
            await Utilities.WriteAndSpeakAsync("What city or zip code do you want the weather for?");
            return;
        }
        else if (Conversation.Current.ActiveTopic == "weather" && !Conversation.Current.AwaitingSelection)
        {
            // This is where we receive the city or zip and handle it
            await InteractionHandling.HandleWeatherCommandAsync(command);
            Conversation.Current.Reset(); // Reset after we're done
            return;
        }
        // google 
        else if (command == "google")
        {
            Conversation.Current.ActiveTopic = "google";
            Conversation.Current.AwaitingSelection = false;
            await Utilities.WriteAndSpeakAsync("What do you want to search?");
            return;
        }
        else if (Conversation.Current.ActiveTopic == "google" && !Conversation.Current.AwaitingSelection)
        {
            await GoogleSearchHandling.HandleGoogleSearchAsync(command);
            Conversation.Current.Reset();
            return;
        }
        // other 
        else if (CommandHandling.CommandHandlers.TryGetValue(command, out Func<Task>? value))
        {
            await value(); // Handle other recognized commands
        }
        else
        {
            await Utilities.WriteAndSpeakAsync($"[Error] [Process] Unknown command. I do not understand {command}.");
        }
    }
    // static can be called without an instance of the class. you don't need to create an object of the class
    // containing this method to call it. A static method belongs to the class itself rather than an instance of the
    // class. 

    public static async Task ReadInputLoopAsync()
    {
        while (true)
        {
            if (!StateFlags.IsEscapeHeld && !StateFlags.IsTyping)
            {
                StateFlags.IsTyping = true;
                string? rawInput = Console.ReadLine();
                StateFlags.IsTyping = false;

                if (!string.IsNullOrWhiteSpace(rawInput))
                {
                    string input = Utilities.NormalizeCommand(rawInput);
                    Console.WriteLine($"[Debug] Input: '{input}'");

                    await ProcessCommandAsync(input);
                }

                await Task.Delay(250);
            }
            else
            {
                await Task.Delay(100);
            }
        }
    }

    public static async Task RecognizeSpeechWhileEscapeHeldAsync(CancellationToken token)
    { // CancellationToken is used to signal that a task should be cancelled 
        var config = SpeechConfig.FromSubscription(Configuration.SpeechKey, Configuration.SpeechRegion);
        using var recognizer = new SpeechRecognizer(config);

        var stopRecognition = new TaskCompletionSource<int>();

        recognizer.Recognized += async (s, e) =>
        // += subscribing to the recognized event so whenever this event is raised, the specified lambda function
        // (s,e) => {...} will be executed. s is sender (recognizer) e is event argument (resulting text and reason
        // for cancellation if any) 
        {
            if (!string.IsNullOrWhiteSpace(e.Result.Text))
            {
                string command = Utilities.NormalizeCommand(e.Result.Text);
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("[You said]: " + command);
                Console.ResetColor();

                await ProcessCommandAsync(command);

                /*
                // Only act on valid one-word commands
                if (StateFlags.currentInputMode == StateFlags.InputMode.AwaitingInput)
                {
                    // Handle input manually here (for weather, google, etc.)
                    StateFlags.currentInputMode = StateFlags.InputMode.Normal;
                    await InteractionHandling.HandleWeatherCommandAsync(command);
                }
                else if (command == "weather")
                {
                    StateFlags.currentInputMode = StateFlags.InputMode.AwaitingInput;
                    StateFlags.currentInputContext = StateFlags.InputContext.Weather;
                    await Utilities.WriteAndSpeakAsync("Please say a city name or ZIP code.");
                }
                else if (CommandHandling.ValidCommands.Contains(command))
                {
                    await ProcessCommandAsync(command);
                }
                else
                {
                    await Utilities.WriteAndSpeakAsync($"[Error] [STT] Unknown command. I do not understand {command}");
                }
                */
            }
        };

        recognizer.Canceled += (s, e) =>
        { // if the recognition session stops 
            // Console.WriteLine("[Voice Recognition] Canceled.");
            stopRecognition.TrySetResult(0);
        }; // 0 marks task as complete. will return false is task has already been completed or canceled 

        recognizer.SessionStopped += (s, e) =>
        { // if the recognition session stops 
            stopRecognition.TrySetResult(0);
        };

        await recognizer.StartContinuousRecognitionAsync(); // starts listening 

        try
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(100, token);
            }
        }
        catch (TaskCanceledException)
        {
            // Expected
        }

        await recognizer.StopContinuousRecognitionAsync(); // stops listening 
        StateFlags.IsTyping = false;
    }
}

public static class FileOperations
{
    public static async Task OpenLogFile()
    {
        string filePath = "log.txt";

        try
        {
            foreach (var process in System.Diagnostics.Process.GetProcessesByName("notepad"))
            { // Try to close any open Notepad instances editing this file
                if (!process.HasExited && process.MainWindowTitle.Contains("log.txt", StringComparison.OrdinalIgnoreCase))
                // foreach (var process in System.Diagnostics.Process.GetProcessesByName("notepad")) gets a list of
                // all running processes with the name "notepad"
                // Process.GetProcessesByName("notepad") returns an array of Process objects that match that name 
                // StringComparison.OrdinalIgnoreCase makes the check case-insensitive, so it matches LOG.TXT,
                // log.TXT, etc.
                // process.WaitForExit(); pauses code until process has fully closed 
                {
                    process.Kill(); // Force close the Notepad window
                    process.WaitForExit(); // Ensure it's closed before continuing
                }
            }

            // Open the log file in Notepad
            if (File.Exists(filePath))
            {
                System.Diagnostics.Process.Start("notepad.exe", filePath);
            }
            else
            {
                await Utilities.WriteAndSpeakAsync("Log file does not exist yet.");
            }
        }
        catch (Exception ex)
        {
            await Utilities.WriteAndSpeakAsync($"[Error] handling log file: {ex.Message}");
        }
    }

    public static async Task CloseLogFile()
    {
        try
        {
            foreach (var process in System.Diagnostics.Process.GetProcessesByName("notepad"))
            {
                if (!process.HasExited && process.MainWindowTitle.Contains("log.txt", StringComparison.OrdinalIgnoreCase))
                {
                    process.Kill(); // Force close Notepad
                    process.WaitForExit(); // Wait for it to close before continuing
                    await Utilities.WriteAndSpeakAsync("Notepad closed.");
                    return;
                }
            }

            await Utilities.WriteAndSpeakAsync("No open Notepad instance found with 'log.txt'.");
        }
        catch (Exception ex)
        {
            await Utilities.WriteAndSpeakAsync($"[Error] closing Notepad: {ex.Message}");
        }
    }
}

public static class BackgroundMonitors
{
    public static async Task CheckEscapeHoldAsync()
    {
        CancellationTokenSource? speechCts = null;
        // This is a CancellationTokenSource object, used to cancel the speech recognition task. It's nullable (?)
        // because it will be initialized when the Escape key is first pressed.
        Task? speechTask = null;
        // This is a Task representing the asynchronous speech recognition task. It’s also nullable, initialized when
        // the Escape key is held down.
        bool previouslyHeld = false;
        // This boolean keeps track of whether the Escape key was held in the previous iteration, which is needed to
        // detect key state changes (held/released).

        while (true)
        {
            bool currentlyHeld = (NativeInterop.GetAsyncKeyState(0x1B) & 0x8000) != 0; // gets state of escape key 

            if (currentlyHeld && !previouslyHeld)
            {
                StateFlags.IsEscapeHeld = true;
                speechCts = new CancellationTokenSource();
                speechTask = Task.Run(() => InputAndCommandHandling.RecognizeSpeechWhileEscapeHeldAsync(speechCts.Token));
                //Starts the speech recognition task RecognizeSpeechWhileEscapeHeldAsync in a background thread,
                //passing the cancellation token to allow stopping the task later.
            }
            else if (!currentlyHeld && previouslyHeld)
            {
                StateFlags.IsEscapeHeld = false;
                // Console.WriteLine("[Escape released] isEscapeHeld now false");

                StateFlags.IsTyping = false;

                if (speechCts != null)
                {
                    speechCts.Cancel();// Requests cancellation of the speech recognition task.
                    if (speechTask != null) await speechTask; // Waits for the speech recognition task to finish
                    speechCts.Dispose(); // Releases resources used by the CancellationTokenSource
                    speechCts = null; // Clears the variables to release memory and prevent any unwanted side effects
                    speechTask = null;
                }
            }

            previouslyHeld = currentlyHeld; // This is necessary for detecting state changes
            await Task.Delay(50);
        }
    }

    public static async Task RunLoggingLoopAsync()
    {
        while (true)
        {
            if (Logger.IsRunning)
            {
                // Console.WriteLine($"[Loop] isEscapeHeld: {isEscapeHeld}, isTyping: {isTyping}, wasPaused: {wasPaused}");

                if (StateFlags.IsEscapeHeld || StateFlags.IsTyping)
                {
                    if (!Logger.WasPaused)
                    {
                        if (StateFlags.IsEscapeHeld)
                            // Console.WriteLine("[Paused] Escape held for push-to-talk");

                            Logger.WasPaused = true;
                    }
                }
                else
                {
                    if (Logger.WasPaused)
                    {
                        // Console.WriteLine("[Resumed]");
                        Logger.WasPaused = false;
                    }

                    // Console.WriteLine("[Loop] Logging temperature...");
                    await Logger.LogTemperature();
                }

                // Console.WriteLine("[Loop] still running...");
                // Console.WriteLine($"[Check] isEscapeHeld: {isEscapeHeld}, isTyping: {isTyping}");
                await Task.Delay(1000);
            }
            else
            {
                await Task.Delay(100);
            }
        }
    }
    // Background Task that checks whether logging should be active with isRunning, checks if it should pause
    // (isEscapeHeld or isTyping), writes the temperature data every second and controls the logic that handles
    // pause or resume and temperature writes 
}

public class SimpleNeuron
{
    // A list of weights — one for each input. These determine how strongly each input influences the output.
    public List<float> weights;

    // The bias is like a threshold adjuster — it nudges the neuron to be more or less likely to activate.
    public float bias; // Marked readonly — it doesn’t change after the neuron is created (by design here).

    // Constructor: takes a list of weights and an optional bias (defaults to 0f).
    public SimpleNeuron(List<float> initialWeights, float bias = 0f)
    {
        this.weights = initialWeights;
        this.bias = bias;
        // These values are saved in the neuron's "memory" for future decisions.
    }

    public float Activate(List<float> inputs)
    {
        // Make sure we have a matching number of weights and inputs.
        // Each input must pair with a weight (like evidence times importance).
        if (inputs.Count != weights.Count)
            throw new ArgumentException("Number of inputs must match number of weights.");

        float sum = 0f; // Start the total at 0.0

        // Loop through each input and multiply it by its matching weight.
        for (int i = 0; i < inputs.Count; i++)
        {
            sum += inputs[i] * weights[i]; // Weighted sum: evidence * importance
        }

        // Add the bias. This shifts the output up or down, like setting how easy it is to "trigger" the neuron.
        sum += bias;

        return Sigmoid(sum);
    }

    // The sigmoid squashes any number into the 0–1 range: perfect for confidence or decision thresholds.
    private float Sigmoid(float x)
    {
        return 1f / (1f + (float)Math.Exp(-x));
        // This formula: 1 / (1 + e^(-x))
        // x = -5 → 0.0067 (very weak signal)
        // x =  0 → 0.5 (neutral signal)
        // x = +5 → 0.993 (very strong signal)
    }
    // inputs * weights, products totaled, add bias, pass output into sigmoid function, output 0 to 1

    public static async Task RunNetworkOnTopWords(Dictionary<string, int> wordCounts, int snippetCount, string query)
    {
        var topWords = wordCounts.OrderByDescending(pair => pair.Value).Take(5).ToList();
        // Extract their values (frequencies) into a list of inputs
        var inputs = topWords.Select(pair => (float)pair.Value).ToList();

        // example weights and a bias (same size as inputs)
        var weightsPerNeuron = new List<List<float>>
        {
            new() { 1f, 0.8f, 0.6f, 0.4f, 0.2f },
            new() { 0.2f, 0.4f, 0.6f, 0.8f, 1f },
            new() { 0.5f, 0.5f, 0.5f, 0.5f, 0.5f },
            new() { 0.3f, 0.7f, 0.2f, 0.6f, 0.4f },
            new() { 0.9f, 0.1f, 0.3f, 0.7f, 0.5f }
        };
        float baseBias = -10f;
        float adjustedBias = baseBias / Math.Max(1, snippetCount); // avoids dividing by 0
        // More data = more confidence in word frequencies.
        // Less data = higher uncertainty = require stronger signals to activate.
        var biases = Enumerable.Repeat(adjustedBias, 5).ToList();

        var network = new SimpleNetwork(weightsPerNeuron, biases);
        var hiddenOutputs = network.Activate(inputs);
        float finalOutput = network.RunOutputNeuron(hiddenOutputs);

        float expectedOutput;
        while (true)
        {
            Console.WriteLine($"\nHow relevant are these top words to your search \"{query}\"?");
            Console.WriteLine($"Words: {string.Join(", ", topWords.Select(p => p.Key))}");
            Console.WriteLine("Type '1' or 'relevant', '0.5' or 'somewhat', '0' or 'not', then press Enter:");

            string? input = Console.ReadLine()?.Trim().ToLower();

            if (input == "1" || input == "relevant")
            {
                expectedOutput = 1.0f;
                break;
            }
            else if (input == "0.5" || input == "somewhat")
            {
                expectedOutput = 0.5f;
                break;
            }
            else if (input == "0" || input == "not")
            {
                expectedOutput = 0.0f;
                break;
            }
            else
            {
                Console.WriteLine("Input not recognized. Please type 1 / 0.5 / 0, or relevant / somewhat / not.");
            }
        }

        await network.TrainBothLayersAsync(hiddenOutputs, expectedOutput);

        Console.WriteLine("\n[Neural Network Analysis]");
        Console.WriteLine($"Calculated bias: {adjustedBias:F2}");
        Console.WriteLine($"Top words: {string.Join(", ", topWords.Select(p => p.Key))}");
        // combines words into one string like "black, hole, dense".
        Console.WriteLine($"Frequencies: {string.Join(", ", inputs)}");
        // prints frequency values used as neuron inputs like " 11, 11, 5".
        Console.WriteLine("Neuron outputs: " + string.Join(", ", hiddenOutputs.Select(o => o.ToString("F3"))));
        // prints the neuron's result between 0 and 1 rounded to 3 decimal places like 0.993
        Console.WriteLine($"Final output neuron result: {finalOutput:F3}");

        // Interpret the output
        string relevance = finalOutput switch
        {
            > 0.9f => "highly relevant", // if finalOutput is greater than 
            > 0.5f => "somewhat relevant", // else if finalOutput is greater than 
            _ => "not relevant" // else 
        };
        string topWordsString = string.Join(", ", topWords.Select(p => p.Key));
        Console.WriteLine($"Hi AJ. Regarding your search \"{query}\", " +
            $"I think the words {topWordsString} are {relevance}");
        // {query} and adding it to the method parameter is the same as await GoogleSearchHandling.HandleGoogleSearchAsync(query);
    }
}

public class SimpleNetwork
// defines a neural network with one hidden layer (5 neurons)
{
    private readonly List<SimpleNeuron> hiddenLayer; // lists the neurons in a hidden layer
    // readonly means once assigned, it can't be replaced 
    private readonly SimpleNeuron outputNeuron; // storing the output neuron inside the SimplyNetwork 

    public SimpleNetwork(List<List<float>> weightsPerNeuron, List<float> biases, List<float>? outputWeights = null, float? outputBias = null) // list of lists because each neuron 
    // has its own set of weights, and a list of biases.the list has 1 list per neuron and 1 bias per neuron 
    {
        // Try to load a full snapshot first. if it exists, it reconstructs the hidden layer and the output
        // nueron using stored weights and biases. if successful, it skips the constructor's fallback logic, 
        // which would otherwise build the network from the provided weights and biases 
        var snap = NetworkStorage.Load();
        if (snap != null)
        {
            hiddenLayer = snap.Hidden
                .Select(h => new SimpleNeuron(h.Weights ?? new List<float>(), h.Bias ?? 0f))
                .ToList();

            var ow = snap.Output.Weights ?? new List<float>();
            var ob = snap.Output.Bias ?? 0f;
            outputNeuron = new SimpleNeuron(ow, ob);
            return;
        }

        // Fallback: build from constructor params
        if (weightsPerNeuron.Count != biases.Count)
            throw new ArgumentException("Each neuron must have a matching bias.");

        hiddenLayer = new List<SimpleNeuron>(); // initializes the hidden layer list 
        for (int i = 0; i < weightsPerNeuron.Count; i++)
        {
            hiddenLayer.Add(new SimpleNeuron(weightsPerNeuron[i], biases[i]));
            // i-th weight list for neuron #0, i-th bias for neuron #0, creates new simpleNeuron with those weights and
            // bias, adds that neuron to the network's internal list of neurons (hiddenLayer) 
        }
        var saved = NetworkStorage.Load(); // May still exist, used for fallback outputNeuron even if the hidden layer wasn't saved

        var defaultOutputWeights = new List<float> { 0.5f, 0.5f, 0.5f, 0.5f, 0.5f };
        float defaultOutputBias = -1f;

        outputNeuron = saved?.Output.Weights != null && saved.Output.Bias.HasValue
            ? new SimpleNeuron(saved.Output.Weights, saved.Output.Bias.Value)
            : new SimpleNeuron(outputWeights ?? defaultOutputWeights, outputBias ?? defaultOutputBias);
        // If we’ve saved any weights and bias, use those. If not, use the ones passed into the network.
        // And if those weren't provided either, then just go with our safe, hardcoded defaults
    }

    public List<float> Activate(List<float> inputs)
    {
        var outputs = new List<float>(hiddenLayer.Count);
        foreach (var neuron in hiddenLayer)
            outputs.Add(neuron.Activate(inputs));
        return outputs;
    } // inputs word frequencies and returns output of each hidden neuron after activation 

    public float RunOutputNeuron(List<float> hiddenOutputs)
        => outputNeuron.Activate(hiddenOutputs);
    // use the outputs from the hidden layer as inputs to the output neuron 

    public async Task<float> TrainBothLayersAsync(List<float> inputs, float expectedOutput, float learningRate = 0.1f)
    {
        // 1) Forward pass
        var hiddenOutputs = Activate(inputs);
        float actualOutput = outputNeuron.Activate(hiddenOutputs);

        // 2) Output layer delta
        float error = expectedOutput - actualOutput;
        float deltaOut = error * actualOutput * (1 - actualOutput);
        // gradient of the output layer using the derivative of the sigmoid, helps calculate how much 
        // each weight needs to be changed 

        Console.WriteLine("\n[Training Mode - Both Layers]");
        Console.WriteLine($"Expected: {expectedOutput:F3}, Actual: {actualOutput:F3}, Error: {error:F3}");

        // 3) ---- Update OUTPUT neuron ----
        for (int j = 0; j < outputNeuron.weights.Count; j++)
        {
            float grad = deltaOut * hiddenOutputs[j];
            float change = learningRate * grad;
            outputNeuron.weights[j] += change;

            Console.WriteLine($"[Output] W{j} += {change:F6} -> {outputNeuron.weights[j]:F6}");
        }

        float outputBiasChange = learningRate * deltaOut;
        outputNeuron.bias += outputBiasChange;
        Console.WriteLine($"[Output] Bias += {outputBiasChange:F6} -> {outputNeuron.bias:F6}");

        // 4) ---- Backprop to HIDDEN layer ----
        for (int j = 0; j < hiddenLayer.Count; j++)
        {
            var hNeuron = hiddenLayer[j];
            float hOut = hiddenOutputs[j];

            // delta_h = (w_j_out * deltaOut) * sigmoid'(hiddenSum) = ... but we already have hOut, so use hOut*(1-hOut)
            float deltaH = outputNeuron.weights[j] * deltaOut * hOut * (1 - hOut);
            // calculates how much each hidden neuron contributed to the output error. this uses the 
            // chain rule of derivatives to propagate error backwards 

            // updates hidden weights and biases 
            for (int i = 0; i < hNeuron.weights.Count; i++)
            {
                float gradHij = deltaH * inputs[i];
                float change = learningRate * gradHij;
                hNeuron.weights[i] += change;
                // each hidden weight updated using the input and the hidden delta 
                // the bias updated with delta only 

                // Optional: log
                Console.WriteLine($"[Hidden {j}] W{i} += {change:F6} -> {hNeuron.weights[i]:F6}");
            }

            float hiddenBiasChange = learningRate * deltaH;
            hNeuron.bias += hiddenBiasChange;
            Console.WriteLine($"[Hidden {j}] Bias += {hiddenBiasChange:F6} -> {hNeuron.bias:F6}");
        }

        // 5) Saves output layer and output neuron 
        await NetworkStorage.SaveAsync(hiddenLayer, outputNeuron);
        // saves the entire network (hidden layer and output neuron) to json for memory and reuse 

        return actualOutput;
    }
}

public class NetworkSnapshot
{
    public List<WeightsAndBiases> Hidden { get; set; } = new();
    public WeightsAndBiases Output { get; set; } = new();
}

public static class NetworkStorage
{
    public const string SnapshotPath = "weights_and_biases.json";

    public static async Task SaveAsync(List<SimpleNeuron> hiddenLayer, SimpleNeuron outputNeuron, string filePath = SnapshotPath)
    {
        var snap = new NetworkSnapshot
        {
            Hidden = hiddenLayer.Select(h => new WeightsAndBiases(h.weights, h.bias)).ToList(),
            Output = new WeightsAndBiases(outputNeuron.weights, outputNeuron.bias)
        };

        var json = System.Text.Json.JsonSerializer.Serialize(snap, new JsonSerializerOptions { WriteIndented = true });

        await File.WriteAllTextAsync(filePath, json);
    }

    public static NetworkSnapshot? Load(string filePath = SnapshotPath)
    {
        if (!File.Exists(SnapshotPath)) return null;
        var json = File.ReadAllText(filePath);
        return System.Text.Json.JsonSerializer.Deserialize<NetworkSnapshot>(json);
    }
}

public class WeightsAndBiases
{
    public List<float>? Weights { get; set; } // allows for json to read or write weights 
    public float? Bias { get; set; } // allows for json to read or write bias 
    // get gets from json file while set writes to json file. json serialization needs get set to be public 

    public WeightsAndBiases() { }
    // default constructor that creates an empty object when loading from a file. json libraries sometimes need this when reconstructing an object step-by-step. when deserializing, it first creates a blank WeightsAndBiases(),
    // then it fills in .Weights = [...] and .Bias = ... from the file, even though you might not call this manually, json does under the hood 

    public WeightsAndBiases(List<float> weights, float bias)
    // custom constructor for creating the object manually in code, pass in list of weights and
    // a bias such as var wnb = new WeightsAndBiases(new List<float> {0.5f, 0.8f}, -1f); 
    {
        Weights = weights;
        Bias = bias;
    }
}

public class WikipediaApiClient
{
    private readonly HttpClient _httpClient;
    private const string BaseUrl = "https://en.wikipedia.org/w/api.php";

    public WikipediaApiClient()
    {
        _httpClient = new HttpClient();
    }
    public async Task<List<string>> GetTopArticleTitlesAsync(string query)
    {
        string encodedQuery = Uri.EscapeDataString(query);
        // new york becomes new%20york, so the url is valid and can be used in a web request
        string url = $"https://en.wikipedia.org/w/api.php?action=query&list=search&format=json&srsearch={encodedQuery}&srlimit=5";

        HttpResponseMessage response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();
        // throws an error is something went wrong, such as a 404 or 500 error

        string json = await response.Content.ReadAsStringAsync();

        using JsonDocument doc = JsonDocument.Parse(json);
        // parses the json into a structured document we can query like a tree 
        var titles = new List<string>();

        foreach (var item in doc.RootElement
                                .GetProperty("query")
                                .GetProperty("search")
                                .EnumerateArray())
        {
            string title = item.GetProperty("title").GetString() ?? "";
            titles.Add(title);
        }
        return titles;
    }

    public async Task<string> GetSummaryForTitleAsync(string title)
    {
        string encodedTitle = Uri.EscapeDataString(title);
        string url = $"https://en.wikipedia.org/w/api.php?action=query&format=json&prop=extracts&exintro=1&explaintext=1&titles={encodedTitle}";

        Console.WriteLine($"[Debug] Wikipedia extract URL: {url}");

        HttpResponseMessage response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();

        string json = await response.Content.ReadAsStringAsync();

        using JsonDocument doc = JsonDocument.Parse(json);
        var pages = doc.RootElement.GetProperty("query").GetProperty("pages");

        foreach (JsonProperty page in pages.EnumerateObject())
        {
            JsonElement pageInfo = page.Value;

            if (pageInfo.TryGetProperty("extract", out JsonElement extractElement))
            {
                string summary = extractElement.GetString() ?? "[No summary found.]";
                Console.WriteLine($"[Debug] Retrieved summary: {summary.Substring(0, Math.Min(80, summary.Length))}...");
                return summary;
            }
        }
        return "[No summary available for this article.]";
    }
}

public static class WikipediaHandling
{
    public static List<string> PendingTitles = new();
    public static async Task HandleWikipediaSearchAsync(string query)
    {
        var wikiClient = new WikipediaApiClient();
        var titles = await wikiClient.GetTopArticleTitlesAsync(query);

        if (titles.Count == 0)
        {
            await Utilities.WriteAndSpeakAsync("I couldn't find any articles for that search.");
            return;
        }

        Console.WriteLine("Top 5 Wikipedia Article Titles:");
        for (int i = 0; i < titles.Count; i++)
        {
            Console.WriteLine($"{i + 1}. {titles[i]}");
        }

        Conversation.Current.ActiveTopic = "wikipedia";
        Conversation.Current.LastSearchQuery = query;
        Conversation.Current.PendingOptions = titles;
        Conversation.Current.AwaitingSelection = true;

        await Utilities.WriteAndSpeakAsync("Please type the number of the article you'd like to read.");
    }
    public static async Task HandleSummarySelectionAsync(string input)
    {
        Console.WriteLine($"[Debug] Entered HandleSummarySelectionAsync with input: {input}");
        var titles = Conversation.Current.PendingOptions;

        if (!int.TryParse(input, out int index) || index < 1 || index > titles.Count)
        {
            await Utilities.WriteAndSpeakAsync("Invalid selection. Please try again.");
            return;
        }

        string selectedTitle = titles[index - 1];
        Console.WriteLine($"[Debug] Selected title: {selectedTitle}");

        var client = new WikipediaApiClient();
        Console.WriteLine("[Debug] Fetching summary from Wikipedia...");
        string summary = await client.GetSummaryForTitleAsync(selectedTitle);

        Console.WriteLine($"\n[{selectedTitle}]\n{summary}");
        await Utilities.WriteAndSpeakAsync($"Here is a summary of {selectedTitle}.");
        Console.WriteLine($"[Debug] Retrieved summary: {(summary?.Substring(0, Math.Min(100, summary.Length)) ?? "null")}...");

        string path = $"{selectedTitle}.json";

        if (File.Exists(path))
        {
            string existingContent = File.ReadAllText(path);

            try
            {
                var existingData = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(existingContent);
                string? existingSummary = existingData != null && existingData.ContainsKey("Summary") ? existingData["Summary"] : null;

                if (existingSummary != summary)
                {
                    File.WriteAllText(path,
                        System.Text.Json.JsonSerializer.Serialize(new { Title = selectedTitle, Summary = summary },new JsonSerializerOptions { WriteIndented = true }));
                    Console.WriteLine("[Updated] Saved new summary.");
                }
                else
                {
                    Console.WriteLine("[Skipped] Summary already up to date.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Error] Failed to parse existing JSON: {ex.Message}");
                // Optionally, overwrite if broken:
                File.WriteAllText(path,System.Text.Json.JsonSerializer.Serialize(new { Title = selectedTitle, Summary = summary },new JsonSerializerOptions { WriteIndented = true }));
                Console.WriteLine("[Recovered] Overwrote corrupted JSON file.");
            }
        }
        else
        {
            File.WriteAllText(path,System.Text.Json.JsonSerializer.Serialize(new { Title = selectedTitle, Summary = summary },new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine("[Saved] Summary saved for the first time.");
        }
        Conversation.Current.Reset();
    }
}

public class ConversationState
{
    public string? ActiveTopic { get; set; } // e.g., "wikipedia", "google"
    public string? LastSearchQuery { get; set; }
    public List<string> PendingOptions { get; set; } = new();
    public bool AwaitingSelection { get; set; } = false;

    public void Reset()
    {
        ActiveTopic = null;
        LastSearchQuery = null;
        PendingOptions.Clear();
        AwaitingSelection = false;
    }
}
