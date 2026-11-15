namespace Phainon.Simulation
{
    /// <summary>
    /// The campaign as shipped: the introduction and the list of chapters. Each mission's own
    /// content is in its own file beside this one, so a new chapter is a new file and one line here.
    /// </summary>
    public static class StoryCampaignContent
    {
        public static StoryCampaignSpec Create()
        {
            return new StoryCampaignSpec
            {
                title = "OPERATION PHAINON",
                subtitle = "THE EXPEDITION INTO THE UNKNOWN",
                introduction = new[]
                {
                    "A young astronaut has been sent on an expedition across the stars.",
                    "Operation Phainon.",
                    "A solitary journey into the unknown, where one astronaut must survive an expedition through the endless darkness of deep space.",
                    "Your objective is simple: obtain the required scientific discoveries, complete your mission and find your way home.",
                    "But why?\n\nThat is for you to discover.",
                    "As you explore the universe, your discoveries could revolutionise the world and transform humanity's understanding of the cosmos.",
                    "Yet science is only part of the mission.\n\nYou are alone.",
                    "Your spacecraft is your home. Your supplies are limited. Your decisions matter.\n\nEvery system must be maintained, every journey carefully considered and every unexpected event survived.",
                    "You may uncover discoveries that change humanity forever.\n\nBut will you make it home?",
                    "SURVIVAL IS JUST AS IMPORTANT AS SCIENCE.",
                    "WHICH WILL YOU CHOOSE?"
                },
                chapters = new[]
                {
                    new StoryChapterSpec { id = "chapter_1", title = "CHAPTER ONE   THE GIANT", missionIds = new[] { Mission01Jupiter.Id } }
                },
                missions = new[] { Mission01Jupiter.Create() }
            };
        }
    }
}
