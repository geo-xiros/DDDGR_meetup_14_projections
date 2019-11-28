using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace cli
{
    internal interface IProjection
    {
        void Projection(Event @event);

        string Result { get; }
    }

    class Program
    {
        static void Main(string[] args)
        {
            var projections = new List<IProjection>()
            {
                new CountEvents(),
                new CountRegisteredUsers(),
                new CountRegisteredUsersPerMonth(),
                new PopularQuizzes(),
                new BotPlayers()
            };

            new EventStore(projections.Select<IProjection, Action<Event>>(p => p.Projection))
                .Replay(FilePathFrom(args));

            foreach (var projection in projections)
            {
                Console.WriteLine(projection.Result);
                Console.WriteLine();
            }

            Console.ReadKey();
        }

        private static string FilePathFrom(string[] args)
        {
            if (args.Length < 1) throw new ArgumentException("Please specify a file to replay");
            return args[0];
        }
    }

    internal class CountEvents : IProjection
    {
        private int _result;

        public void Projection(Event @event)
        {
            _result++;
        }

        public string Result => $"number of events: {_result}";
    }

    internal class CountRegisteredUsers : IProjection
    {
        private int _result;

        public void Projection(Event @event)
        {
            if (@event.Type.Equals("PlayerHasRegistered", StringComparison.InvariantCultureIgnoreCase))
            {
                _result++;
            }
        }

        public string Result => $"number of registered users: {_result}";
    }

    internal class CountRegisteredUsersPerMonth : IProjection
    {
        private Dictionary<string, int> _result = new Dictionary<string, int>(); // month -> count

        public void Projection(Event @event)
        {
            if (!@event.Type.Equals("PlayerHasRegistered", StringComparison.InvariantCultureIgnoreCase))
            {
                return;
            }

            var month = @event.Timestamp.ToString("yyyy-MM");
            if (_result.TryGetValue(month, out var currentValue))
            {
                _result[month] = currentValue + 1;
            }
            else
            {
                _result.Add(month, 1);
            }
        }

        public string Result => string.Join(Environment.NewLine,
            _result.Select(kvp => $"{kvp.Key} : {kvp.Value}").OrderBy(t => t));
    }

    internal class PopularQuizzes : IProjection
    {
        private Dictionary<string, string> _quizTitles = new Dictionary<string, string>(); // quiz id => quiz title
        private Dictionary<string, string> _gameQuizzes = new Dictionary<string, string>(); //game id => quiz id
        private Dictionary<string, int> _quizCount = new Dictionary<string, int>(); // quiz id => game count

        public void Projection(Event @event)
        {
            switch (@event.Type)
            {
                case "QuizWasCreated":
                    _quizTitles.Add(@event.Payload["quiz_id"], @event.Payload["quiz_title"]);
                    _quizCount.Add(@event.Payload["quiz_id"], 0);
                    break;

                case "GameWasOpened":
                    _gameQuizzes.Add(@event.Payload["game_id"], @event.Payload["quiz_id"]);
                    break;

                case "GameWasStarted":
                    var quiz_id = _gameQuizzes[@event.Payload["game_id"]];
                    if (_quizCount.TryGetValue(quiz_id, out var count))
                    {
                        _quizCount[quiz_id] = count + 1;
                    }
                    break;
            }
        }

        public string Result => string.Join(Environment.NewLine,
            _quizCount.OrderByDescending(q => q.Value).Take(10).Select(qp => $"{_quizTitles[qp.Key]} ({qp.Key}): {qp.Value}"));
    }

    internal class BotPlayers : IProjection
    {
        // games with total answears time => game_id, answear total time 
        private Dictionary<string, Player> _players = new Dictionary<string, Player>();
        private Dictionary<string, DateTime> _questionsAskedAt = new Dictionary<string, DateTime>();

        public class Player
        {
            public Player(string firstName, string lastName)
            {
                FirstName = firstName;
                LastName = lastName;
            }
            public Dictionary<string, double> TotalTimeByGame { get; set; } = new Dictionary<string, double>();

            public string FirstName { get; }
            public string LastName { get; }
            public override string ToString()
            {
                return $"{LastName} {FirstName}";
            }
        }

        public void Projection(Event @event)
        {
            string gameId = string.Empty;
            string playerId = string.Empty;
            string questionId = string.Empty;
            Player player = null;
            double totalTime = 0;

            switch (@event.Type)
            {
                case "PlayerHasRegistered":
                    playerId = @event.Payload["player_id"];
                    _players.Add(playerId,
                        new Player(@event.Payload["first_name"],
                                   @event.Payload["last_name"]));
                    break;

                case "PlayerJoinedGame":
                    gameId = @event.Payload["game_id"];
                    playerId = @event.Payload["player_id"];

                    player = _players[playerId];
                    if (!player.TotalTimeByGame.TryGetValue(gameId, out totalTime))
                    {
                        player.TotalTimeByGame.Add(gameId, 0);
                    }
                    break;

                case "GameWasStarted":
                    break;
                case "GameWasCancelled":
                    break;
                case "GameWasFinished":
                    break;
                case "QuestionWasAsked":
                    questionId = @event.Payload["question_id"];
                    _questionsAskedAt[questionId] = @event.Timestamp;
                    break;

                case "AnswerWasGiven":
                    gameId = @event.Payload["game_id"];
                    playerId = @event.Payload["player_id"];
                    questionId = @event.Payload["question_id"];

                    player = _players[playerId];
                    player.TotalTimeByGame.TryGetValue(gameId, out totalTime);

                    var questionAskedAt = _questionsAskedAt[questionId];
                    var timeToAnswer = (@event.Timestamp - questionAskedAt).TotalSeconds;

                    player.TotalTimeByGame[gameId] = totalTime + timeToAnswer;

                    break;
            }
        }
        public string Result => string.Join(Environment.NewLine,
            _players.Values
                .Where(g => g.TotalTimeByGame.Values.Sum() == 0));
    }
}