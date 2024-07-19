## Run the project
Replace the "SubscriptionKey" and "Region" in appsettings.json

dotnet build

dotnet run

## Project setup: 
dotnet new webapi -n speech-demo-be-dotnet

cd speech-demo-be-dotnet

dotnet add package Microsoft.CognitiveServices.Speech

dotnet add package Microsoft.AspNetCore.Cors

dotnet add package Xabe.FFmpeg

dotnet run

## Updates:
Update the controller to invoke the openAI API. 


