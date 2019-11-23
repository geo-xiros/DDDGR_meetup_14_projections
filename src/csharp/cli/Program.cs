using System;
using System.Collections.Generic;
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
        private Dictionary<string, Game> games = new Dictionary<string, Game>();
        private Dictionary<string, Player> players = new Dictionary<string, Player>();

        public class Game
        {
            public double TotalAnswerTime { get; set; }
            public bool Started { get; set; }
            public Dictionary<string, Question> Questions { get; set; } = new Dictionary<string, Question>();
            public IEnumerable<KeyValuePair<string, double>> TimesByPlayer
                => Questions
                    .Values
                    .SelectMany(qt => qt.PlayerAnswerTime)
                    .GroupBy(
                        q => q.Key,
                        q => q.Value,
                        (q, v) => new KeyValuePair<string, double>(q, v.Sum()));
        }

        public class Question
        {
            public DateTime AskedTime { get; set; }
            public Dictionary<string, double> PlayerAnswerTime { get; set; } = new Dictionary<string, double>();
        }

        public class Player
        {
            public string FirstName { get; set; }
            public string LastName { get; set; }
            public override string ToString()
            {
                return $"{LastName} {LastName}";
            }
        }

        public void Projection(Event @event)
        {
            Game game;

            switch (@event.Type)
            {
                case "PlayerHasRegistered":
                    players.Add(@event.Payload["player_id"],
                        new Player()
                        {
                            FirstName = @event.Payload["first_name"],
                            LastName = @event.Payload["last_name"]
                        });
                    break;

                case "GameWasStarted":
                    games[@event.Payload["game_id"]] = new Game();
                    break;

                case "QuestionWasAsked":
                    game = games[@event.Payload["game_id"]];
                    game.Questions.Add(@event.Payload["question_id"],
                        new Question() { AskedTime = @event.Timestamp });
                    break;

                case "AnswerWasGiven":
                    game = games[@event.Payload["game_id"]];
                    var questionTimes = game.Questions[@event.Payload["question_id"]];
                    var timeToAnswer = (@event.Timestamp - questionTimes.AskedTime).TotalSeconds;

                    if (!questionTimes.PlayerAnswerTime.TryGetValue(@event.Payload["player_id"], out double playerAnswerTime))
                    {
                        questionTimes.PlayerAnswerTime.Add(@event.Payload["player_id"], timeToAnswer);
                    }
                    else
                    {
                        questionTimes.PlayerAnswerTime[@event.Payload["player_id"]] += timeToAnswer;
                    }

                    break;
            }
        }

        public string Result => string.Join(Environment.NewLine,
            games.Values
            .SelectMany(g => g.TimesByPlayer.Where(t => t.Value == 0).Select(t => t))
            .Join(players, g => g.Key, p => p.Key, (g, p) => $"{g.Value} {p.Value.FirstName} {p.Value.LastName}")
            .Distinct()
            .OrderBy(p=>p));
    }
}