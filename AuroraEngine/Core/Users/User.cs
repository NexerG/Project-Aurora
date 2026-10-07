namespace ArctisAurora.Core.Users
{
    // A person a file names: a ticket's creator or assignee.
    public sealed class User
    {
        public static readonly User current = new User("Grexen");

        public readonly string name;

        public User(string name) => this.name = name;

        // First letters of the first two words, or the first two letters of a one-word name.
        public string initials
        {
            get
            {
                string[] words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                string letters = words.Length switch
                {
                    0 => "?",
                    1 => words[0][..Math.Min(2, words[0].Length)],
                    _ => $"{words[0][0]}{words[1][0]}"
                };
                return letters.ToUpperInvariant();
            }
        }

        // The current user for their own name, otherwise a user of that name.
        public static User Named(string name) => name == current.name ? current : new User(name);

        public override bool Equals(object? obj) => obj is User other && other.name == name;

        public override int GetHashCode() => name.GetHashCode();
    }
}
