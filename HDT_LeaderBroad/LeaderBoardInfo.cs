using System;

namespace HDT_LeaderBoard
{
    public class LeaderBoardInfo
    {
        public String name { get; set; }
        public String rank { get; set; }
        public String score { get; set; }

        public LeaderBoardInfo(String name, String rank, String score)
        {
            this.name = name;
            this.rank = rank;
            this.score = score;
        }

        public override string ToString() => $"({name}, {rank}, {score})";
    }
}
