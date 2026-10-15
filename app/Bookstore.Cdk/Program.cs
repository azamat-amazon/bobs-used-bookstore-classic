using Amazon.CDK;
using Bookstore.Cdk;
using Bookstore.Common;

var app = new App();

var env = MakeEnv();

var coreStack = new CoreStack(app, $"{Constants.AppName}Core", new StackProps { Env = env });
var networkStack = new NetworkStack(app, $"{Constants.AppName}Network", new StackProps { Env = env });
var databaseStack = new DatabaseStack(app, $"{Constants.AppName}Database", new DatabaseStackProps { Env = env, Vpc = networkStack.Vpc });
var ecsStack = new EcsStack(app, $"{Constants.AppName}ECS", new EcsStackProps { Env = env, Vpc = networkStack.Vpc, Database = databaseStack.Database, ImageBucket = coreStack.ImageBucket, WebAppUserPool = coreStack.WebAppUserPool });

app.Synth();

static Amazon.CDK.Environment MakeEnv(string account = null, string region = null)
{
    return new Amazon.CDK.Environment
    {
        Account = account ?? System.Environment.GetEnvironmentVariable("CDK_DEFAULT_ACCOUNT"),
        Region = region ?? System.Environment.GetEnvironmentVariable("CDK_DEFAULT_REGION")
    };
}
