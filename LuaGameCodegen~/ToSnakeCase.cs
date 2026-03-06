namespace LuaGameCodegen
{
    internal static class SnakeCaseHelper
    {
        public static string ToSnakeCase(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            var sb = new System.Text.StringBuilder();
            for (var i = 0; i < input.Length; i++)
            {
                var c = input[i];
                if (char.IsUpper(c))
                {
                    if (i > 0 && !char.IsUpper(input[i - 1]))
                        sb.Append('_');
                    else if (i > 0 && i < input.Length - 1
                             && char.IsUpper(input[i - 1])
                             && char.IsLower(input[i + 1]))
                        sb.Append('_');
                    sb.Append(char.ToLowerInvariant(c));
                }
                else
                {
                    sb.Append(c);
                }
            }

            return sb.ToString();
        }
    }
}
