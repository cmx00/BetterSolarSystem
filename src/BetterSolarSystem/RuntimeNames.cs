namespace BetterSolarSystem
{
    public static class RuntimeNames
    {
        public static bool Restore(PlanetData planet, string authoredName)
        {
            if (planet == null || string.IsNullOrEmpty(authoredName)) return false;
            // User renames live in overrideName. Keep them intact; repair only the
            // native fallback name which RegenerateName replaces during loading.
            planet.name = StandardName(authoredName);
            return true;
        }

        public static string StandardName(string authoredName)
        {
            // Match only our former defaults; arbitrary player names remain intact.
            switch (authoredName)
            {
                case "木星 · 星海之冠": return "木星";
                case "木卫一 · 烬火之诗": return "木卫一";
                case "木卫二 · 青穹之约": return "木卫二";
                case "木卫三 · 霜海之镜": return "木卫三";
                case "木卫四 · 暮雪之庭": return "木卫四";
                case "木卫五 · 遗落星砂": return "木卫五";
                case "土卫二 · 冰湖": return "土卫二";
                case "土卫六 · 荒原": return "土卫六";
                case "土卫八 · 冻土": return "土卫八";
                case "谷神星 · 小行星带": return "谷神星";
                case "冥王星 · 柯伊伯带": return "冥王星";
                case "卡戎 · 冥河摆渡": return "卡戎";
                default: return authoredName;
            }
        }
    }
}
