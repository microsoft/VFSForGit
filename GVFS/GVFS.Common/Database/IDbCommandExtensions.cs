using System.Data;

namespace GVFS.Common.Database
{
    public static class IDbCommandExtensions
    {
        public static IDbDataParameter AddParameter(this IDbCommand command, string name, DbType dbType, object value)
        {
            IDbDataParameter parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.DbType = dbType;
            parameter.Value = value;
            command.Parameters.Add(parameter);
            return parameter;
        }
    }
}
