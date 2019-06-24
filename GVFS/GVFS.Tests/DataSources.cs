namespace GVFS.Tests
{
    public class DataSources
    {
        public static object[] AllBools
        {
            get
            {
                return new object[]
                {
                     new object[] { true },
                     new object[] { false },
                };
            }
        }

        public static object[] IntegerModes(int num)
        {
            object[] modes = new object[num];
            for (int i = 0; i < num; i++)
            {
                modes[i] = new object[] { i };
            }

            return modes;
        }
    }
}
