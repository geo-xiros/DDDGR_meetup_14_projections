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
        // players mapping => player_id, last_name, first_name
        private Dictionary<string, string> _players = new Dictionary<string, string>();

        // questions mapping => question_id, answear
        //private Dictionary<string, string> _questions = new Dictionary<string, string>();

        // games with total answears time => game_id, answear total time 
        private Dictionary<string, double> _games = new Dictionary<string, double>();

        private Dictionary<string, string> _playerByGame = new Dictionary<string, string>();

        // questions asked => question_id, time started
        private Dictionary<string, DateTime> _questionsAsked = new Dictionary<string, DateTime>();

        public void Projection(Event @event)
        {

            switch (@event.Type)
            {
                //case "QuestionAddedToQuiz":
                //    _questions.Add(@event.Payload["question_id"], @event.Payload["answer"]);
                //    break;

                case "PlayerHasRegistered":
                    _players.Add(@event.Payload["player_id"], $"{@event.Payload["last_name"]} {@event.Payload["first_name"]}");
                    break;

                case "GameWasStarted":
                    _games.Add(@event.Payload["game_id"], 0);
                    break;
                case "QuestionWasAsked":
                    _questionsAsked[@event.Payload["question_id"]] = @event.Timestamp;
                    break;
                case "AnswerWasGiven":
                    var gameId = @event.Payload["game_id"];
                    var questionId = @event.Payload["question_id"];
                    //var answer = @event.Payload["answer"];

                    //if (_games[gameId] == -1)
                    //{
                    //    return;
                    //}

                    //_questions.TryGetValue(questionId, out string correctAnswer);

                    //if (answer != correctAnswer)
                    //{
                    //    _games[gameId] = -1;
                    //    break;
                    //}

                    if (!_questionsAsked.TryGetValue(questionId, out DateTime startTime))
                    {
                        return;
                    }

                    var answearedTime = @event.Timestamp;
                    var dif = (answearedTime - startTime).TotalSeconds;

                    _games[gameId] += dif;
                    _playerByGame[gameId] = _players[@event.Payload["player_id"]];
                    break;
            }
        }

        public string Result => string.Join(Environment.NewLine, _games
            .Where(g => g.Value == 0)
            .OrderBy(g => g.Value)
            .Join(_playerByGame, g => g.Key, p => p.Key, (g, p) => $"Player {p.Value} is a bot."));
    }
}