using Ascendant;
using Microsoft.CognitiveServices.Speech;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Buffers.Text;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using static InputAndCommandHandling;
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
            " 'close' to close it, 'weather' for weather, 'affection' for affection, " +
            "'time' for time, 'wiki' for wiki, or 'exit' to quit.");

        await Task.Delay(-1); // Keep the main thread alive. creates a task that doesn't complete until it's 
        // explicitly canceled or terminated. -1 is a special value that indicates an indefinite delay. doesn't
        // allow the thread to terminate 
    }
}
public static class CommandHandling
{
    public static readonly HashSet<string> ValidCommands = ["start", "stop", "open", "close", "weather",
        "affection", "time", "wiki", "exit"];
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
    public static string SpeechKey => ApiKeys.SpeechKey;
    public static string SpeechRegion => ApiKeys.SpeechRegion;
    public static string WeatherApiKey => ApiKeys.WeatherApiKey;
    public static string LocationKey => ApiKeys.LocationKey;
    public static string GoogleApiKey => ApiKeys.GoogleApiKey;
    public static string SearchEngineId => ApiKeys.SearchEngineId;
}
public static class StateFlags
{
    public static class Conversation
    {
        public static ConversationState Current = new();
    }

    public static bool IsEscapeHeld = false; // Checks whether the escape key is currently held down 
    public static bool IsTyping = false; // Tracks if you're actively entering text input 
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
public class ConversationState
{
    public string? ActiveTopic { get; set; }
    public string? LastSearchQuery { get; set; }
    public List<string> PendingOptions { get; set; } = new();
    public bool AwaitingSelection { get; set; } = false;
    public bool AwaitingRelevanceFeedback { get; set; } = false;
    public string? RelevanceFeedbackTopic { get; set; }


    public void Reset()
    {
        ActiveTopic = null;
        LastSearchQuery = null;
        PendingOptions.Clear();
        AwaitingSelection = false;
        AwaitingRelevanceFeedback = false;
        RelevanceFeedbackTopic = null;
    }
}
public static class RelevanceFeedbackHandler
{
    public static float? LastOutput = null;
    public static List<float>? LastHiddenOutputs = null;
    public static Dictionary<string, int>? LastTopWords = null;
    public static string? LastQuery = null;
}
public static class InputAndCommandHandling
{
    public static async Task ProcessCommandAsync(string command)
    {
        if (Conversation.Current.AwaitingRelevanceFeedback)
        {
            string topic = Conversation.Current.RelevanceFeedbackTopic ?? "";

            if (topic == "wikipedia")
            {
                if (command.ToLowerInvariant() is "yes" or "no")
                {
                    bool wasHelpful = command.ToLowerInvariant() == "yes";
                    await WikipediaHandling.HandleWikipediaRelevanceAsync(Conversation.Current.LastSearchQuery ?? "", wasHelpful);
                    return;
                }

                await Utilities.WriteAndSpeakAsync("Please say yes or no.");
                return;
            }
        }

        // wikipedia 
        else if (command == "wiki")
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
            await WikipediaHandling.GetFullArticleForTitleAsync(command);
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
    public static class QueryClassifier
    {
        // Utilities.NormalizeCommand => return input.Trim().ToLower().TrimEnd('.', '!', '?');

        // Stop words (common words that don't carry meaning, used for filtering)
        private static readonly HashSet<string> StopWords = new HashSet<string>
        {
            "the", "a", "an", "and", "but", "or", "to", "of", "in", "on", "with", "at", "for", "by", "as", "from", "it", "its",
            "am", "is", "are", "was", "were", "be", "been", "being", "has", "have", "had", "do", "does", "did", "doing",
            "he", "she", "they", "we", "you", "I", "me", "us", "him", "her",
            "this", "that", "these", "those", "each", "every", "some", "any", "no",
            "all", "both", "few", "more", "less", "much", "many", "several",
            "oh", "ah", "wow", "hey",
            "isn't", "aren't", "didn't", "don't", "doesn't", "wasn't", "weren't", "haven't", "hasn't", "I'm", "you're", "he's", "she's", "it's",
            "just", "only", "really", "very", "too", "quite", "so", "now", "yet", "still", "ever", "again", "here", "there"
        };

        private static readonly HashSet<string> QuestionWords = new HashSet<string>
        {
            "what", "who", "how", "why", "when", "is", "are", "can", "does", "do"
        };

        public static List<string> ExtractMainKeywords(string query)
        {
            string normalizedQuery = Utilities.NormalizeCommand(query);  // Normalize the query to lowercase
            var words = normalizedQuery.Split(' ');  // Split the query into words

            // Filter out stop words and query words
            var mainKeywords = words.Where(word => word.Length > 1 && !StopWords.Contains(word) && !QuestionWords.Contains(word))
                                     .ToList();

            return mainKeywords;
        }

        public static string ClassifyQuery(string query)
        {
            string normalizedQuery = Utilities.NormalizeCommand(query);

            // Check for question words
            var words = normalizedQuery.Split(' ');
            foreach (var word in words)
            {

                if (QuestionWords.Contains(word))
                {
                    if (word == "what" || word == "who")
                    {
                        return "fact"; // Expecting a factual query, like "What is the weather?"
                    }
                    else if (word == "how" || word == "why" || word == "does" || word == "do")
                    {
                        return "explanation"; // Expecting a process explanation, like "How does photosynthesis work?"
                    }
                    else if (word == "is" || word == "can" || word == "are")
                    {
                        return "yesno"; // Expecting a yes/no question, like "Is it raining?"
                    }
                    else if (word == "when")
                    {
                        return "date"; // Expecting a time related question, like "when was the civil war"
                    }
                }
            }

            return "unknown"; // If no question word is found
        }
        public static float AdjustScoreBasedOnQueryType(float originalScore, string queryType, string title, string query)
        {
            float adjustedScore = originalScore;
            title = title.ToLowerInvariant();

            var mainKeywords = ExtractMainKeywords(query);
            // Boost score based on the presence of main keywords in the title
            foreach (var keyword in mainKeywords)
            {
                if (title.Contains(keyword))  // If the title contains the keyword
                {
                    adjustedScore *= 1.2f;  // Boost the score for relevant titles
                }
            }

            // Define logic to adjust scores before multiplication
            switch (queryType)
            {
                case "fact":
                    // Boost for keywords that typically represent factual information
                    if (title.Contains("capital") || title.Contains("location") || title.Contains("discovered") || title.Contains("name") || title.Contains("definition"))
                        adjustedScore *= 1.3f; // Boost for factual queries
                    break;

                case "explanation":
                    // Boost for titles focusing on explaining processes or mechanisms
                    if (title.Contains("process") || title.Contains("mechanism") || title.Contains("cause") || title.Contains("function") || title.Contains("system"))
                        adjustedScore *= 1.2f; // Boost for explanation-based queries
                    break;

                case "yesno":
                    // For yes/no questions, boost for titles with definitive answers
                    if (title.Contains("yes") || title.Contains("no") || title.Contains("true") || title.Contains("false") || title.Contains("allowed") || title.Contains("possible"))
                        adjustedScore *= 1.1f; // Slightly increase for yes/no-related answers
                    break;

                case "date":
                    // For date-related queries, boost for historical or time-based terms
                    if (title.Contains("history") || title.Contains("date") || title.Contains("century") || title.Contains("era") || title.Contains("timeline"))
                        adjustedScore *= 1.4f; // Boost for time-related queries
                    break;

                default:
                    break;
            }
            // Console.WriteLine($"Query Type: {queryType}");  // Log the query type (fact, explanation, etc.)
            // Console.WriteLine("Main Keywords: " + string.Join(", ", mainKeywords));
            return adjustedScore;
        }
    }
    public static async Task ReadInputLoopAsync()
    {
        while (true)
        {
            if (!StateFlags.IsEscapeHeld)
            {
                if (Console.KeyAvailable)
                {
                    StateFlags.IsTyping = true;

                    try
                    {
                        string? rawInput = Console.ReadLine();

                        if (!string.IsNullOrWhiteSpace(rawInput))
                        {
                            string input = Utilities.NormalizeCommand(rawInput);
                            await ProcessCommandAsync(input);
                        }
                    }
                    finally
                    {
                        StateFlags.IsTyping = false;
                    }

                    await Task.Delay(250);
                }
                else
                {
                    await Task.Delay(50); // small delay if no key yet
                }
            }
            else
            {
                await Task.Delay(100); // Escape is held
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
                Console.WriteLine(command);
                Console.ResetColor();

                await ProcessCommandAsync(command);
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
                    Console.WriteLine($"[Debug] {IsTyping}");
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
    public async Task<string> GetFullArticleForTitleAsync(string title)
    {
        string encodedTitle = Uri.EscapeDataString(title);
        string url = $"https://en.wikipedia.org/w/api.php?action=query&format=json&prop=extracts&explaintext=1&titles={encodedTitle}";

        HttpResponseMessage response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();

        string json = await response.Content.ReadAsStringAsync();

        using JsonDocument doc = JsonDocument.Parse(json);
        var pages = doc.RootElement.GetProperty("query").GetProperty("pages");

        foreach (JsonProperty page in pages.EnumerateObject())
        {
            if (page.Value.TryGetProperty("extract", out JsonElement extractElement))
            {
                return extractElement.GetString() ?? "[No content found.]";
            }
        }

        return "[No content available for this article.]";
    }
    public async Task<string> GetSummaryForTitleAsync(string title)
    {
        string encodedTitle = Uri.EscapeDataString(title);
        string url = $"https://en.wikipedia.org/w/api.php?action=query&format=json&prop=extracts&exintro=1&explaintext=1&titles={encodedTitle}";

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

        // Console.WriteLine("Top 5 Wikipedia Article Titles:");
        for (int i = 0; i < titles.Count; i++)
        {
            // Console.WriteLine($"{i + 1}. {titles[i]}");
        }

        Conversation.Current.ActiveTopic = "wikipedia";
        Conversation.Current.LastSearchQuery = query;
        Conversation.Current.PendingOptions = titles;
        // Conversation.Current.AwaitingSelection = true;

        // await Utilities.WriteAndSpeakAsync("Please type the number of the article you'd like to read.");

        await WikipediaHandling.HandleWikipediaRelevanceAsync(Conversation.Current.LastSearchQuery ?? "");
    }
    public static async Task GetFullArticleForTitleAsync(string input)
    {
        var titles = Conversation.Current.PendingOptions;

        if (!int.TryParse(input, out int index) || index < 1 || index > titles.Count)
        {
            await Utilities.WriteAndSpeakAsync("Invalid selection. Please try again.");
            return;
        }

        string selectedTitle = titles[index - 1];
        // Console.WriteLine($"[Debug] Selected title: {selectedTitle}");

        var client = new WikipediaApiClient();
        // Console.WriteLine("[Debug] Fetching summary from Wikipedia...");
        string fullArticle = await client.GetFullArticleForTitleAsync(selectedTitle);

        // Console.WriteLine($"\n[{selectedTitle}]\n{summary}");
        // await Utilities.WriteAndSpeakAsync($"Here is a summary of {selectedTitle}.");
        // Console.WriteLine($"[Debug] Retrieved summary: {(summary?.Substring(0, Math.Min(100, summary.Length)) ?? "null")}...");

        string path = $"{selectedTitle}.json";

        var articleData = new
        {
            Title = selectedTitle,
            FullArticle = fullArticle
        };

        string newJson = System.Text.Json.JsonSerializer.Serialize(articleData, new JsonSerializerOptions { WriteIndented = true });

        if (File.Exists(path))
        {
            string existingContent = File.ReadAllText(path);
            if (existingContent != newJson)
            {
                File.WriteAllText(path, newJson);
                Console.WriteLine("[Updated] Saved new full article.");
            }
            else
            {
                Console.WriteLine("[Skipped] Full article already up to date.");
            }
        }
        else
        {
            File.WriteAllText(path, newJson);
            Console.WriteLine("[Saved] Full article saved for the first time.");
        }

        Conversation.Current.Reset();
    }
    public static async Task HandleSummarySelectionAsync(string input)
    {
        // Console.WriteLine($"[Debug] Entered HandleSummarySelectionAsync with input: {input}");
        var titles = Conversation.Current.PendingOptions;

        if (!int.TryParse(input, out int index) || index < 1 || index > titles.Count)
        {
            await Utilities.WriteAndSpeakAsync("Invalid selection. Please try again.");
            return;
        }

        string selectedTitle = titles[index - 1];
        // Console.WriteLine($"[Debug] Selected title: {selectedTitle}");

        var client = new WikipediaApiClient();
        // Console.WriteLine("[Debug] Fetching summary from Wikipedia...");
        string summary = await client.GetSummaryForTitleAsync(selectedTitle);

        // Console.WriteLine($"\n[{selectedTitle}]\n{summary}");
        // await Utilities.WriteAndSpeakAsync($"Here is a summary of {selectedTitle}.");
        // Console.WriteLine($"[Debug] Retrieved summary: {(summary?.Substring(0, Math.Min(100, summary.Length)) ?? "null")}...");

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
                        System.Text.Json.JsonSerializer.Serialize(new { Title = selectedTitle, Summary = summary }, new JsonSerializerOptions { WriteIndented = true }));
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
                File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(new { Title = selectedTitle, Summary = summary }, new JsonSerializerOptions { WriteIndented = true }));
                Console.WriteLine("[Recovered] Overwrote corrupted JSON file.");
            }
        }
        else
        {
            File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(new { Title = selectedTitle, Summary = summary }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine("[Saved] Summary saved for the first time.");
        }
        Conversation.Current.Reset();
    }
    public static async Task HandleWikipediaRelevanceAsync(string query, bool? feedback = null)
    {
        var wikiClient = new WikipediaApiClient();
        var titles = await wikiClient.GetTopArticleTitlesAsync(query);

        if (titles.Count == 0)
        {
            await Utilities.WriteAndSpeakAsync("I couldn't find any articles for that search.");
            return;
        }

        var queryWords = StandardizeWords(query).ToHashSet();

        Console.WriteLine("Title Relevance Scores:");
        var scoredTitles = new List<(string Title, float Score)>();

        foreach (var title in titles)
        {
            /*
            var titleWords = StandardizeWords(title).ToHashSet();
            float score = CalculateJaccardSimilarity(queryWords, titleWords);
            scoredTitles.Add((title, score));
            Console.WriteLine($"- {title} ␦ Score: {score:F3}");
            */

            var titleWords = StandardizeWords(title).ToHashSet();
            float score = CalculateJaccardSimilarity(queryWords, titleWords);

            // Adjust the score based on query type
            string queryType = QueryClassifier.ClassifyQuery(query);
            // Console.WriteLine(query);
            // Console.WriteLine($"Query Type: {queryType}");  // Log to check if it's correctly classified as "fact"
            float adjustedScore = QueryClassifier.AdjustScoreBasedOnQueryType(score, queryType, title, query);

            scoredTitles.Add((title, adjustedScore));
            Console.WriteLine($"[Debug] - {title} ␦ Adjusted Score: {score:F3}");
            Console.WriteLine($"- {title} ␦ Adjusted Score: {adjustedScore:F3}");

            // query classification, jaccard similarity, adjusts score of title, feeds as ann input 
        }

        // 🌸 Top 5 scores as ANN inputs
        var inputs = scoredTitles
            .OrderByDescending(t => t.Score)
            .Take(5)
            .Select(t => t.Score)
            .ToList();

        while (inputs.Count < 5)
            inputs.Add(0f);

        Console.WriteLine("[Debug] ANN Inputs: " + string.Join(", ", inputs.Select(i => i.ToString("F3"))));

        // 🌸 Run network: hidden → output
        var network = NeuralNetworkManager.Network;
        var hiddenOutputs = network.Activate(inputs);
        float finalOutput = network.RunOutputNeuron(hiddenOutputs);

        // 🌸 Store for feedback/training
        RelevanceFeedbackHandler.LastOutput = finalOutput;
        RelevanceFeedbackHandler.LastHiddenOutputs = hiddenOutputs;
        RelevanceFeedbackHandler.LastTopWords = scoredTitles
            .OrderByDescending(t => t.Score)
            .Take(5)
            .ToDictionary(p => p.Title, p => (int)(p.Score * 100)); // use % score as int
        RelevanceFeedbackHandler.LastQuery = query;

        // 🌸 Friendly output
        string relevance = finalOutput switch
        {
            > 0.9f => "highly relevant",
            > 0.5f => "somewhat relevant",
            _ => "not relevant"
        };

        var top5Titles = scoredTitles
            .OrderByDescending(t => t.Score)
            .Take(5)
            .Select(t => t.Title)
            .ToList();

        var outputWeights = network.OutputNeuron.weights;

        // Calculate weighted influence of each title input
        var influences = inputs
            .Select((inputValue, i) => inputValue * outputWeights[i])
            .ToList();

        int mostInfluentialIndex = influences.IndexOf(influences.Max());
        string mostInfluentialTitle = top5Titles[mostInfluentialIndex];

        string topTitles = string.Join(", ", RelevanceFeedbackHandler.LastTopWords.Keys);
        // Console.WriteLine($"Regarding your search \"{query}\", I think the titles {topTitles} are {relevance}.");
        // await Utilities.WriteAndSpeakAsync($"I think the articles I found are {relevance} to your question.");

        Conversation.Current.ActiveTopic = "wiki_feedback";

        // 🌸 Feedback
        await Utilities.WriteAndSpeakAsync($"Of the articles I found, I believe \"{mostInfluentialTitle}\" contributed most to my answer.");
        Console.Write("Was this helpful? (yes/no): ");

        Conversation.Current.AwaitingRelevanceFeedback = true;
        Conversation.Current.RelevanceFeedbackTopic = "wikipedia";
    }
    public static async Task HandleWikipediaRelevanceAsync(string query, bool wasHelpful)
    {
        if (RelevanceFeedbackHandler.LastTopWords == null)
        {
            await Utilities.WriteAndSpeakAsync("Sorry, I don’t have the previous article information to learn from.");
            return;
        }

        var inputs = RelevanceFeedbackHandler.LastTopWords.Values
            .Take(5)
            .Select(score => (float)score / 100f)
            .ToList();

        var mostInfluentialTitle = RelevanceFeedbackHandler.LastTopWords.Keys.First(); // or store explicitly if needed
        int mostInfluentialIndex = 0;

        var focusedInputs = new List<float> { 0f, 0f, 0f, 0f, 0f };
        focusedInputs[mostInfluentialIndex] = inputs[mostInfluentialIndex];

        float expectedOutput = wasHelpful ? 1f : 0f;

        Console.WriteLine($"[Training] Focusing on \"{mostInfluentialTitle}\" (index {mostInfluentialIndex}) with score {inputs[mostInfluentialIndex]:F3}");
        await NeuralNetworkManager.Network.TrainBothLayersAsync(focusedInputs, expectedOutput);

        await NetworkStorage.SaveAsync(
            NeuralNetworkManager.Network.HiddenNeurons,
            NeuralNetworkManager.Network.OutputNeuron);

        Console.WriteLine("[Training] Thank you. I've learned from your feedback");

        await GetFullArticleForTitleAsync((mostInfluentialIndex + 1).ToString());

        // Clear feedback state
        Conversation.Current.AwaitingRelevanceFeedback = false;
        Conversation.Current.RelevanceFeedbackTopic = null;
    }

    private static float CalculateJaccardSimilarity(HashSet<string> set1, HashSet<string> set2)
    {
        var intersection = set1.Intersect(set2).Count();
        // counts how many words are in both sets (intersection) 
        var union = set1.Union(set2).Count();
        // counts how many unique words are in either set (union)
        return union == 0 ? 0f : (float)intersection / union;
        // if union is 0, return 0 to avoid division by zero, otherwise return the ratio of
        // intersection to union
    }
    public static IEnumerable<string> StandardizeWords(string input)
    // can move to utilities later if desired to reuse code. here for now for testing and to simplify 
    {
        return input
            .ToLower()
            .Split(new[] { ' ', '-', '_', ',', '.', ':', ';', '(', ')', '[', ']', '"', '\'' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(word => word.TrimEnd('s')) // crude stemmer
            .Where(word => word.Length > 1);
    }
}

#region Artifical Neural Network
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
        if (!File.Exists(filePath)) return null;
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
public class NetworkSnapshot
{
    public List<WeightsAndBiases> Hidden { get; set; } = new();
    public WeightsAndBiases Output { get; set; } = new();
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
}
public class SimpleNetwork
// defines a neural network with one hidden layer (5 neurons)
{
    public List<SimpleNeuron> HiddenNeurons => hiddenLayer;
    public SimpleNeuron OutputNeuron => outputNeuron;

    private readonly List<SimpleNeuron> hiddenLayer; // lists the neurons in a hidden layer
    // readonly means once assigned, it can't be replaced 
    private readonly SimpleNeuron outputNeuron; // storing the output neuron inside the SimplyNetwork 
    public static NetworkSnapshot? LoadSnapshot(string path = "weights_and_biases.json")
    {
        return NetworkStorage.Load(path);
    }

    public SimpleNetwork(List<List<float>> weightsPerNeuron, List<float> biases, List<float>? outputWeights = null, float? outputBias = null) // list of lists because each neuron 
    // has its own set of weights, and a list of biases.the list has 1 list per neuron and 1 bias per neuron 
    {
        // Try to load a full snapshot first. if it exists, it reconstructs the hidden layer and the output
        // nueron using stored weights and biases. if successful, it skips the constructor's fallback logic, 
        // which would otherwise build the network from the provided weights and biases 
        var snap = LoadSnapshot();
        if (snap != null && snap.Hidden.Count > 0 && snap.Output?.Weights != null && snap.Output.Bias.HasValue)
        {
            hiddenLayer = snap.Hidden
                .Select(h => new SimpleNeuron(h.Weights ?? new List<float>(), h.Bias ?? 0f))
                .ToList();

            outputNeuron = new SimpleNeuron(snap.Output.Weights, snap.Output.Bias.Value);
            return;
        }

        // Fallback: build from constructor params
        if (weightsPerNeuron.Count != biases.Count)
            throw new ArgumentException("Each neuron must have a matching bias.");

        hiddenLayer = new List<SimpleNeuron>(); // initializes the hidden layer list 
        for (int i = 0; i < weightsPerNeuron.Count; i++)
            hiddenLayer.Add(new SimpleNeuron(weightsPerNeuron[i], biases[i]));
        // i-th weight list for neuron #0, i-th bias for neuron #0, creates new simpleNeuron with those weights and
        // bias, adds that neuron to the network's internal list of neurons (hiddenLayer) 

        var defaultOutputWeights = new List<float> { 0.5f, 0.5f, 0.5f, 0.5f, 0.5f };
        float defaultOutputBias = -1f;

        outputNeuron = new SimpleNeuron(outputWeights ?? defaultOutputWeights, outputBias ?? defaultOutputBias);
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
public static class NeuralNetworkManager
{
    public static SimpleNetwork Network { get; }

    static NeuralNetworkManager()
    // static constructor runs once when the class is first accessed, initializes the Network property
    {
        var snapshot = NetworkStorage.Load("weights_and_biases.json");

        if (snapshot != null)
        {
            var weights = snapshot.Hidden.Select(h => h.Weights ?? new List<float>()).ToList();
            var biases = snapshot.Hidden.Select(h => h.Bias ?? 0f).ToList();
            Network = new SimpleNetwork(weights, biases, snapshot.Output.Weights, snapshot.Output.Bias);
        }
        else
        {
            var defaultWeights = new List<List<float>>
            {
                new() { 1f, 0.8f, 0.6f, 0.4f, 0.2f },
                new() { 0.2f, 0.4f, 0.6f, 0.8f, 1f },
                new() { 0.5f, 0.5f, 0.5f, 0.5f, 0.5f },
                new() { 0.3f, 0.7f, 0.2f, 0.6f, 0.4f },
                new() { 0.9f, 0.1f, 0.3f, 0.7f, 0.5f }
            };
            var defaultBiases = Enumerable.Repeat(-2f, 5).ToList();
            var defaultOutputWeights = new List<float> { 0.5f, 0.5f, 0.5f, 0.5f, 0.5f };
            var defaultOutputBias = -1f;
            Network = new SimpleNetwork(defaultWeights, defaultBiases, defaultOutputWeights, defaultOutputBias);
        }
    }
}
#endregion

// enhance natural language processing layer, then advance to classifier neural network  
// integrate neural network to answer query through wikipedia 
// add neural network (public  class IntentAnalyzer)to determine intent (command, question, other) 

/*
    public static string NormalizeCommand(string input)
    {
        return input.Trim().ToLower().TrimEnd('.', '!', '?');
    }

    and this code, can later expand and combine perhaps 

    public static IEnumerable<string> StandardizeWords(string input)
    // can move to utilities later if desired to reuse code. here for now for testing and to simplify 
    {
        return input
            .ToLower()
            .Split(new[] { ' ', '-', '_', ',', '.', ':', ';', '(', ')', '[', ']', '"', '\'' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(word => word.TrimEnd('s')) // crude stemmer
            .Where(word => word.Length > 1);
    }
*/
