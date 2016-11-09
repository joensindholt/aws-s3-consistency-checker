using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Amazon.S3;
using Amazon.S3.Model;
using Newtonsoft.Json;

namespace AwsS3ConsistencyChecker
{
    /// <summary>
    /// Idea: Create a solution that checks S3 consistency by spawning two threads
    /// One thread that writes objects to S3
    /// One thread that reads the written objects from S3 when notified about write completion from thread 1
    ///
    /// After reading this: https://aws.amazon.com/about-aws/whats-new/2015/08/amazon-s3-introduces-new-usability-enhancements/
    /// and this: http://docs.aws.amazon.com/AmazonS3/latest/dev/Introduction.html#Regions
    /// I wanted to test out what will happen in the case described above
    /// </summary>
    public class Program
    {
        private const int startWrite = 0;
        private const int writes = 100;
        private const string testFilesDirectory = "test-files-in";
        private const string outputDirectory = "test-files-out";

        private static List<Tuple<int, PutObjectResponse, Exception>> _writeErrors = new List<Tuple<int, PutObjectResponse, Exception>>();
        private static List<Tuple<int, GetObjectResponse, Exception>> _readErrors = new List<Tuple<int, GetObjectResponse, Exception>>();

        private static IAmazonS3 client;

        public static void Main(string[] args)
        {
            string awsAccessKeyId = args[0];
            string awsSecretAccessKey = args[1];
            client = new AmazonS3Client(awsAccessKeyId, awsSecretAccessKey, Amazon.RegionEndpoint.EUWest1);

            var broker = new Broker();

            InitializeTestFileDirectories();

            // Reading thread
            var readThread = Task.Run(() =>
            {
                Console.WriteLine("(2) Running read thread");

                var writeThreadDone = false;

                broker.Subscribe("write-thread-ended", (identifier) =>
                {
                    Console.WriteLine("(2) Write thread ended recieved");
                    writeThreadDone = true;
                });

                broker.Subscribe("write-completed", (identifier) =>
                {
                    Console.WriteLine("(2) Write completed recieved: " + identifier);
                    ReadObjectFromS3(identifier).Wait();
                });

                while (!writeThreadDone)
                {
                }

                Console.WriteLine("(2) Ending read thread");
            });

            // Writing thread
            var writeThread = Task.Run(() =>
            {
                Console.WriteLine("(1) Running write thread");

                // Wait a little to allow read thread to subscribe to messages
                Thread.Sleep(50);

                for (var i = startWrite; i < startWrite + writes; i++)
                {
                    CreateTestFile(i);
                    try
                    {
                        PutObjectInS3(i).Wait();
                        broker.Notify("write-completed", i);
                    }
                    catch
                    {
                        // ignore - logged elsewhere
                    }
                }

                Console.WriteLine("(1) Ending write thread");
                broker.Notify("write-thread-ended");
            });

            Task.WaitAll(readThread, writeThread);

            PrintStats();
        }

        private static void InitializeTestFileDirectories()
        {
            if (!Directory.Exists(testFilesDirectory))
            {
                Directory.CreateDirectory(testFilesDirectory);
            }

            if (!Directory.Exists(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }
        }

        private static void PrintStats()
        {
            Console.WriteLine("Write errors occured: " + _writeErrors.Count);
            Console.WriteLine("Read errors occured: " + _readErrors.Count);

            string statsFile = "stats.txt";

            using (FileStream stream = File.Create(statsFile))
            {
                using (StreamWriter writer = new StreamWriter(stream))
                {
                    writer.WriteLine("Write errors occured: " + _writeErrors.Count);
                    writer.WriteLine("Read errors occured: " + _readErrors.Count);
                    writer.WriteLine("-----------------------------------------");
                    writer.WriteLine("Write errors:");
                    writer.WriteLine("-----------------------------------------");
                    _writeErrors.ForEach(error =>
                    {
                        writer.WriteLine("Identifier: " + error.Item1);
                        writer.WriteLine("Response: ");
                        writer.WriteLine(JsonConvert.SerializeObject(error.Item2));
                        writer.WriteLine("Exception: ");
                        writer.WriteLine(JsonConvert.SerializeObject(error.Item3));
                    });

                    writer.WriteLine("-----------------------------------------");
                    writer.WriteLine("Read errors:");
                    writer.WriteLine("-----------------------------------------");
                    _readErrors.ForEach(error =>
                    {
                        writer.WriteLine("Identifier: " + error.Item1);
                        writer.WriteLine("Response: ");
                        writer.WriteLine(JsonConvert.SerializeObject(error.Item2));
                        writer.WriteLine("Exception: ");
                        writer.WriteLine(JsonConvert.SerializeObject(error.Item3));
                    });
                }
            }
        }

        private static void CreateTestFile(int identifier)
        {
            string filePath = GetTestFilePath(identifier);
            File.WriteAllText(filePath, fileContents);
        }

        private static async Task PutObjectInS3(int identifier)
        {
            PutObjectRequest request = new PutObjectRequest()
            {
                BucketName = "readwriteconsistencytest",
                Key = identifier.ToString(),
                FilePath = GetTestFilePath(identifier)
            };

            PutObjectResponse response = null;
            try
            {
                response = await client.PutObjectAsync(request);
            }
            catch (Exception ex)
            {
                _writeErrors.Add(new Tuple<int, PutObjectResponse, Exception>(identifier, response, ex));
                throw ex;
            }
        }

        private static async Task ReadObjectFromS3(int identifier)
        {
            GetObjectRequest request = new GetObjectRequest()
            {
                BucketName = "readwriteconsistencytest",
                Key = identifier.ToString()
            };

            GetObjectResponse response = null;
            try
            {
                response = await client.GetObjectAsync(request);

                if (response.HttpStatusCode != System.Net.HttpStatusCode.OK)
                {
                    throw new Exception("Did not get OK response when reading object");
                }

                await response.WriteResponseStreamToFileAsync($"{outputDirectory}/{identifier}.txt", false, new CancellationToken());
            }
            catch (Exception ex)
            {
                _readErrors.Add(new Tuple<int, GetObjectResponse, Exception>(identifier, response, ex));
            }
        }

        private static string GetTestFilePath(int identifier)
        {
            return $"{testFilesDirectory}/testfile_{identifier}.jpg";
        }

        // Yes - this should be in a file, but I'm lazy here
        private const string fileContents = @"Lorem ipsum dolor sit amet, consectetur adipiscing elit. Ut accumsan accumsan tempor. Class aptent taciti sociosqu ad litora torquent per conubia nostra, per inceptos himenaeos. Nullam semper sapien nulla, quis iaculis sem consequat nec. Curabitur efficitur facilisis augue, id consectetur est dictum a. Vivamus vulputate leo et ante tempor, at finibus elit congue. Nulla vitae mauris elit. Aenean sed ipsum massa. Suspendisse egestas diam sapien, non facilisis enim rutrum quis. Curabitur ut rutrum magna. Pellentesque felis magna, feugiat id nisi eu, pellentesque ultricies dolor. Nunc tincidunt sem sit amet risus commodo pharetra. Morbi euismod, libero non imperdiet rhoncus, metus metus suscipit metus, eu gravida ligula nisi et ante. Donec pretium, leo at volutpat pellentesque, nibh diam tristique urna, ac lacinia libero erat eu mauris. Maecenas volutpat maximus nunc.

Mauris elementum luctus nibh, sit amet aliquet orci dictum at. Mauris non arcu id est congue vulputate. Phasellus facilisis fermentum libero vel placerat. Donec id mauris quam. Nullam dictum risus eget metus lobortis dapibus. Nunc a turpis a nulla suscipit congue. Sed ornare efficitur sem sit amet finibus. Fusce at consequat ligula. Aliquam gravida augue eget commodo varius. In a leo in nulla sollicitudin eleifend. Mauris eget gravida nisi, vel rutrum erat.

Praesent ultricies euismod nisi, in auctor dui luctus sit amet. Fusce nisi lacus, ornare vel sollicitudin vel, placerat non elit. Curabitur non libero eu lacus sollicitudin suscipit. Interdum et malesuada fames ac ante ipsum primis in faucibus. Vestibulum ante ipsum primis in faucibus orci luctus et ultrices posuere cubilia Curae; Ut blandit, lorem eget vestibulum pellentesque, lorem dolor porttitor sapien, sit amet suscipit nisi lacus vitae lacus. Duis blandit lacus eu elementum vestibulum. Vivamus efficitur lacus vel dolor consequat, sit amet hendrerit lacus iaculis. Curabitur eget nibh a dolor tincidunt dictum. Phasellus rhoncus vel arcu feugiat porta. Curabitur facilisis imperdiet nisl vel accumsan. Mauris facilisis turpis ut nulla ultricies gravida.

Ut lacinia mattis diam, in imperdiet augue scelerisque sed. Mauris porttitor, dui quis porta commodo, nisi nisl pretium nulla, vitae congue sem ipsum sit amet dui. Phasellus sagittis risus urna, ut vulputate ligula semper ut. Nam faucibus leo justo, ac lobortis velit feugiat at. Lorem ipsum dolor sit amet, consectetur adipiscing elit. Proin eget lorem sed justo sagittis aliquet. Vestibulum sapien lectus, sollicitudin eget odio eu, porta eleifend augue. Fusce in ex felis. Aliquam imperdiet eget turpis sed bibendum. Donec ullamcorper varius lacus et vestibulum. In hac habitasse platea dictumst. Donec turpis sem, dignissim id arcu ut, ultrices ultricies magna. Donec in erat sit amet quam hendrerit pharetra ac id lorem. Morbi efficitur justo non dui lobortis vestibulum.

Ut rutrum quam ut imperdiet bibendum. Mauris ut sapien porta, hendrerit dolor at, ullamcorper eros. Curabitur rutrum neque vel ante lobortis suscipit. Nulla vel tortor id odio volutpat pharetra. Phasellus luctus tristique vehicula. Praesent non hendrerit metus, volutpat tempor ipsum. Praesent bibendum vehicula urna nec scelerisque. Ut tempor nulla dignissim massa scelerisque, a condimentum ex lobortis.

Donec auctor leo nec tellus scelerisque porttitor. Duis at volutpat sapien. Vivamus ac cursus massa, in ornare arcu. Pellentesque suscipit vitae ante non mollis. Donec tincidunt, tortor a aliquam sagittis, purus libero sodales est, sed consectetur lorem massa ac quam. Vivamus ac molestie tortor, quis maximus diam. Nullam vel vestibulum enim. Donec bibendum erat ante, ac vulputate lectus fermentum sit amet. Proin sit amet ipsum sit amet enim suscipit lobortis sed eget sapien. Vivamus mi odio, aliquet quis placerat vitae, tincidunt vitae tortor. Suspendisse et convallis magna. Mauris eu sem vel risus sollicitudin finibus.

Mauris mollis ut eros eget elementum. Donec blandit, purus id dignissim dapibus, velit metus laoreet mauris, vel eleifend nisl libero vel eros. Proin eget erat eu diam posuere tincidunt. Phasellus luctus eleifend massa, a euismod orci suscipit et. Sed a est mauris. Suspendisse quis maximus quam. Donec a pretium urna. Vivamus semper magna at sapien elementum, in bibendum felis dignissim. Sed sed dolor orci. Duis nisi metus, placerat sit amet mauris vitae, vehicula ultrices urna. Nam vel enim eget elit dapibus vestibulum id in neque. Duis risus arcu, sagittis nec enim ac, ultrices dictum diam. Nulla facilisis, lacus ut malesuada elementum, neque erat semper libero, et bibendum elit diam et augue. Ut viverra turpis nisi, quis ultricies dolor dignissim et. Nullam tempus viverra vehicula.

Donec sit amet neque ac ex tincidunt dapibus ac id magna. Curabitur mauris quam, rhoncus quis libero a, tincidunt auctor lorem. Donec eget dictum tortor. Vestibulum elementum tincidunt velit at malesuada. Fusce volutpat, ligula vitae pretium blandit, urna mi molestie erat, ullamcorper dictum lacus sem vitae augue. Maecenas accumsan mattis tincidunt. Ut non scelerisque libero, vitae mattis ligula. Suspendisse quis lobortis odio. Donec convallis lorem diam, vel tristique sem pharetra non. Etiam condimentum scelerisque felis sed efficitur. Praesent lorem diam, posuere id ipsum sit amet, sagittis mollis purus. Mauris feugiat nunc vitae velit varius semper. Aliquam ut convallis magna. Maecenas ut neque malesuada sem pharetra ullamcorper.

Integer sed malesuada dolor. Aliquam vitae augue sed lacus commodo mattis. Ut consequat ligula non magna consectetur cursus. Nulla at fringilla sem, in sollicitudin augue. Donec iaculis molestie felis vel pellentesque. Integer mollis enim ut purus pharetra, eget scelerisque neque vulputate. Fusce sed nisl quis augue eleifend condimentum sit amet vel arcu.

Curabitur accumsan nisi nibh, non consectetur elit imperdiet sit amet. Ut ultrices eros aliquam nunc scelerisque gravida. Nulla ligula est, rutrum sit amet pharetra vitae, cursus eget erat. Maecenas ac vulputate sem. Nunc ac libero maximus, mollis orci non, interdum leo. Curabitur eu diam tortor. Aliquam pellentesque viverra efficitur. Proin blandit sem eget sem congue, vel vehicula velit rutrum. Pellentesque habitant morbi tristique senectus et netus et malesuada fames ac turpis egestas. Curabitur id risus luctus, ultricies ligula in, imperdiet lorem. Donec ut nibh sit amet ligula tempus eleifend. Aliquam non velit at libero mattis feugiat sed eget nulla. Aenean consequat luctus pharetra.

Morbi mattis urna non nisl faucibus luctus. Quisque laoreet mi nec felis sagittis pretium. Nulla tempus, est cursus vulputate congue, justo leo convallis risus, efficitur ullamcorper elit dolor non massa. Vestibulum ac nunc lectus. Nunc et blandit dui. Donec porta, nunc quis gravida ultricies, augue nisl efficitur sem, consequat molestie ipsum mi varius sem. Sed id maximus ex, eu tristique arcu. Nullam non tincidunt justo, a rhoncus arcu. Donec laoreet malesuada libero id consectetur. Mauris vehicula maximus ultrices. Aenean feugiat hendrerit erat et maximus. Ut tortor felis, mollis ac neque quis, euismod fringilla enim. Donec eu orci nec arcu scelerisque auctor. Duis sit amet sodales ante, eget ullamcorper lacus. Nulla facilisi.

Integer turpis nibh, vulputate sit amet arcu vitae, viverra iaculis dui. Etiam aliquet, elit aliquam pulvinar lobortis, sem nulla varius quam, suscipit posuere sapien leo vel magna. In eu consectetur ex. Integer sed ligula luctus, cursus justo in, egestas turpis. Etiam maximus ante quis orci finibus, eu vulputate urna sodales. Morbi scelerisque justo ut viverra pharetra. Mauris magna eros, rhoncus et mauris sit amet, molestie posuere urna. Nullam vitae ligula id sem venenatis ultrices. Aliquam cursus vel risus nec convallis. Aliquam nec tincidunt tellus, eget fringilla orci. Vestibulum sed efficitur risus. Suspendisse orci turpis, dignissim non pellentesque et, blandit sed libero. Curabitur eu imperdiet lectus, sed pulvinar elit. Nunc nec tempor ante. Suspendisse accumsan mauris ac ipsum semper efficitur.

Pellentesque rhoncus mauris velit. Integer nec nisl dapibus sapien mattis iaculis. Vestibulum aliquet at ipsum ullamcorper elementum. In hac habitasse platea dictumst. Nulla tincidunt mollis laoreet. Cras sed enim quam. Ut rutrum mollis purus nec ultricies. Cras et quam efficitur, porta erat in, tristique nisi. Donec et sapien a mi faucibus sodales. Praesent vestibulum malesuada eros, ac consequat ligula tincidunt non. In in ultrices odio. Nullam fermentum pellentesque sem vel convallis. Nam congue purus nec auctor vulputate.

Vestibulum vitae sapien dui. Mauris luctus hendrerit urna, vitae accumsan nisi dignissim eu. Quisque vitae nunc vitae libero efficitur varius a in ex. Mauris porttitor sodales elit vel lacinia. Curabitur ultricies a tellus nec pellentesque. In vestibulum sit amet sapien id consequat. In hac habitasse platea dictumst. Maecenas pellentesque volutpat velit, sed dictum massa maximus eget. Phasellus quis ligula eget est interdum lacinia vel quis odio. Ut efficitur aliquet lectus, ac fermentum mauris viverra vitae. In a nunc at dui pharetra tincidunt. Nulla sed varius diam, nec viverra dolor. Interdum et malesuada fames ac ante ipsum primis in faucibus. Suspendisse at euismod enim, ut imperdiet nisl. Pellentesque habitant morbi tristique senectus et netus et malesuada fames ac turpis egestas.

Curabitur ullamcorper ultrices ante, ac posuere ipsum convallis quis. Aenean sollicitudin, magna in fringilla tincidunt, lectus metus lacinia nisl, sit amet finibus lectus dui in felis. Quisque fermentum eget nisi non pharetra. Quisque eu facilisis libero. Nullam vitae tellus quis nisi feugiat cursus eget et nunc. Duis aliquam, ligula quis posuere tempus, libero massa placerat dolor, eu facilisis nulla enim sit amet tellus. Maecenas lacus nulla, bibendum et vestibulum vulputate, gravida semper lorem. Donec vel dolor eget tortor dapibus viverra vel eu sapien. Nam quis orci a leo vulputate pharetra. Phasellus dignissim venenatis purus, in pretium sem sollicitudin quis. Lorem ipsum dolor sit amet, consectetur adipiscing elit. Pellentesque et eleifend justo, a vestibulum tortor. In sollicitudin sollicitudin placerat. Cras dapibus purus dui, eu accumsan neque varius eu. Cras lorem dolor, viverra at tortor eu, tristique aliquet turpis. Mauris posuere nisi enim, nec varius nisl placerat eu.

Praesent rutrum lorem ac nunc finibus ornare. Vivamus tincidunt, dolor sagittis fermentum congue, risus augue facilisis est, ac rhoncus quam neque nec nisl. Integer at tristique ex, nec rhoncus neque. Lorem ipsum dolor sit amet, consectetur adipiscing elit. Donec dapibus gravida nisl non dictum. In vel nibh id elit dictum dictum finibus eget odio. Proin maximus, tortor non hendrerit luctus, felis arcu tincidunt nisl, vitae congue magna lectus non eros. Suspendisse potenti. Proin laoreet nisi nunc, non ornare metus dapibus porta. Pellentesque quis pellentesque sapien. Sed quis iaculis eros, in porttitor sem. Ut non neque ut magna fringilla semper. Maecenas sed vestibulum arcu. Mauris magna augue, gravida sed sollicitudin vel, pellentesque non libero. Integer interdum, ante sit amet pretium laoreet, mi libero sollicitudin nisi, ut cursus turpis nisl eget ligula.

Suspendisse eros leo, consectetur lobortis commodo eu, placerat a purus. Fusce tellus nulla, rhoncus et eros non, lobortis dapibus lacus. Donec tristique egestas elit a semper. Sed at lorem vulputate, vestibulum tellus non, sodales ligula. Donec vel tellus dignissim, finibus augue eget, mollis elit. Vivamus eu mauris eu lectus suscipit rutrum vel vehicula est. Ut non nisi vel enim imperdiet sagittis. Ut arcu lectus, tincidunt ac arcu a, porttitor tempor ligula.

Vivamus viverra nibh ac leo sollicitudin ultricies. Nullam tincidunt pulvinar magna ut suscipit. Sed finibus molestie lectus vitae malesuada. Ut cursus porttitor dolor nec convallis. Vivamus est metus, varius at sagittis vel, aliquam sit amet diam. Curabitur vitae metus nunc. Suspendisse id lectus sit amet odio vestibulum varius vitae eget nisl. In quis dignissim nunc, eu auctor tortor. Phasellus a erat magna. Duis vitae felis hendrerit, tempor enim ut, mollis est. Maecenas ut quam neque.

Nam bibendum lobortis mi, non elementum urna consectetur nec. Phasellus ut sem neque. Duis interdum quam hendrerit, faucibus turpis id, porttitor tellus. Aliquam dignissim ut lectus sed vehicula. Quisque nec placerat eros. Ut iaculis nisl sit amet lacus scelerisque, ac rutrum purus rutrum. In egestas elit sapien, non semper velit viverra quis. Duis molestie magna ac felis pharetra, id porttitor leo lobortis. Curabitur tristique purus leo, cursus finibus diam consectetur vel. Duis laoreet ligula at erat vehicula mollis nec eget orci. Ut tristique pellentesque ante, non dapibus libero scelerisque eget. Phasellus vitae elementum velit, id fermentum nibh. Mauris eget semper sem.

Morbi accumsan ligula odio, et feugiat est pharetra at. Aliquam eget rhoncus enim. Integer lobortis velit a risus euismod aliquam. Nam non pharetra mauris, at interdum augue. Nulla justo arcu, lacinia et est ut, maximus commodo quam. Etiam vel aliquet nisi, sed bibendum erat. Cras sed sollicitudin lacus. Integer varius ultrices augue, non pretium neque iaculis nec. Phasellus elementum mi at ipsum iaculis, at elementum nisl pretium. Nulla luctus quis nisi ac vulputate. Donec ullamcorper tincidunt lectus vel ultricies. Proin erat libero, eleifend et elementum vel, egestas et ligula. Aliquam massa sem, rhoncus ut malesuada sed, pellentesque vestibulum dui. Aliquam ipsum est, ornare vel velit eget, placerat efficitur arcu. Integer condimentum leo vel velit ultrices fermentum. Suspendisse id lacinia ex.

Cras malesuada lobortis tincidunt. Nulla tristique eros non dui imperdiet, non congue augue accumsan. Morbi vitae sapien non sapien mollis semper varius ac nibh. Quisque lobortis est sed felis fermentum, quis ornare neque suscipit. Duis dictum euismod libero et ultricies. Quisque et arcu odio. Suspendisse bibendum metus ipsum, non lacinia orci placerat tincidunt. Praesent tortor erat, hendrerit sed pellentesque at, volutpat at tellus. Etiam est nisl, condimentum eu rutrum id, consequat eu elit. In et ornare leo.

Vivamus in ligula purus. Integer imperdiet sagittis nunc a pharetra. Suspendisse viverra faucibus pulvinar. Donec non bibendum nulla, sed laoreet mauris. Mauris tincidunt nibh aliquet tellus malesuada auctor. Vestibulum vestibulum odio lacus, id mattis tellus lobortis vitae. Sed eleifend erat lectus, sit amet bibendum justo pulvinar vitae. Suspendisse congue leo mauris, scelerisque rhoncus metus euismod lacinia. Sed venenatis sagittis malesuada. Ut commodo consequat arcu sed venenatis. Curabitur bibendum rhoncus vehicula. Nullam aliquet euismod mauris eget gravida.

Quisque a euismod nulla. Sed vitae tortor euismod, fringilla lorem a, cursus magna. Maecenas a porta ligula, non pretium ex. Mauris sollicitudin facilisis tortor sed tincidunt. Ut quis ex dignissim, congue ante ac, viverra felis. Sed eget dui ac metus ultricies fermentum ac at purus. Mauris in venenatis mi. Fusce egestas risus ultrices molestie hendrerit. Phasellus non consectetur nisl, id ultricies orci. Phasellus porta tristique vehicula. Vestibulum cursus enim at lacus suscipit, ac vestibulum sem efficitur. Fusce at tortor lectus. Curabitur vitae massa quis risus vestibulum sollicitudin a ac justo. Nunc accumsan congue pulvinar. Cum sociis natoque penatibus et magnis dis parturient montes, nascetur ridiculus mus. Donec eget feugiat neque, nec placerat magna.

Donec ut eleifend nisl. Aenean et tincidunt ligula. Nam elementum risus eu tellus malesuada, id sodales est consequat. Maecenas facilisis diam ex, a consectetur turpis semper vel. Integer non ultrices ipsum, nec pulvinar sapien. Duis in porta nunc. Nunc scelerisque rhoncus consectetur.

Donec elementum iaculis ex. Etiam nec elit maximus, vehicula tortor in, fringilla lectus. Integer dictum viverra nibh, id tempus leo varius nec. Etiam pulvinar ipsum justo. Proin ullamcorper sagittis metus. Fusce laoreet dolor mi, sed faucibus odio semper ut. Donec sed ante posuere, cursus velit vitae, vehicula tellus. Mauris eget placerat nunc. Praesent vel semper ipsum, vitae blandit leo. Phasellus eget urna feugiat, bibendum erat sit amet, luctus purus. Curabitur bibendum, justo eu congue faucibus, quam ante efficitur ipsum, imperdiet facilisis lacus mauris sit amet velit. In hac habitasse platea dictumst.

Class aptent taciti sociosqu ad litora torquent per conubia nostra, per inceptos himenaeos. Vivamus maximus feugiat posuere. Etiam tincidunt varius purus, in efficitur ipsum tempor nec. Praesent tincidunt iaculis tellus ut aliquam. Suspendisse lobortis, arcu sit amet ultrices feugiat, leo nisi posuere ex, et auctor ipsum turpis et eros. Praesent vitae scelerisque dolor, et pulvinar urna. Vestibulum erat erat, malesuada sed commodo sit amet, porttitor quis turpis. Phasellus id blandit elit. Pellentesque vel risus turpis. In a mauris in orci hendrerit malesuada at a erat. Donec eu convallis neque, eget fermentum magna. Nulla mattis sagittis magna. Fusce tristique lorem lorem, in viverra turpis elementum non. Ut bibendum enim id urna suscipit, non eleifend tellus bibendum.

Aliquam sed elit eu ex faucibus suscipit dignissim eget lectus. Sed consequat diam nulla. Maecenas nec lacus a ex egestas suscipit eu eu purus. Aliquam ante libero, commodo imperdiet luctus non, placerat consequat mauris. Etiam gravida lorem ac nulla pharetra, vel malesuada risus rhoncus. Phasellus dictum dolor et mauris tincidunt laoreet. Nam ut suscipit tortor, vel sodales sapien. Quisque tincidunt at purus eget pharetra. Nam eget urna iaculis velit tristique mollis. In quis turpis velit.

Nullam congue risus neque, a feugiat mi venenatis vitae. Sed viverra porttitor eros vel aliquam. Integer auctor vel arcu vitae venenatis. Ut nec turpis sed leo rutrum sollicitudin. Duis ornare mi in dui facilisis feugiat. Fusce quis sapien sapien. In at volutpat purus. Pellentesque habitant morbi tristique senectus et netus et malesuada fames ac turpis egestas. Cras molestie risus in tortor pellentesque, ac rhoncus purus venenatis. Etiam mollis, libero in tempus tincidunt, enim eros pharetra arcu, sed commodo lacus ipsum ac lacus. Nulla feugiat eros quis libero commodo, eu pellentesque lectus aliquam. Donec feugiat nunc ut purus pellentesque tempor. Curabitur at leo posuere, tincidunt neque vitae, congue enim.

Vivamus metus nisi, vehicula vitae odio feugiat, sollicitudin elementum est. Sed placerat turpis ut eros scelerisque, ac varius eros viverra. Nulla suscipit mattis orci, id porttitor augue. Nulla eu sem quis lacus facilisis commodo. Nulla condimentum egestas erat. Donec condimentum vel justo non tincidunt. Cras tempus augue nisl, euismod scelerisque lacus consequat eu. Pellentesque varius varius ornare. In malesuada elit sem, sit amet condimentum nisi commodo at. Proin vitae leo malesuada, iaculis turpis a, molestie leo. Nam id iaculis eros. Pellentesque ac urna pretium, pretium tortor sed, pulvinar metus. Quisque semper sapien in lorem consequat mollis. Praesent laoreet sapien in mollis volutpat. Maecenas nec tortor porttitor, convallis elit non, mollis justo.

Donec rutrum, nibh nec vulputate scelerisque, magna nisl hendrerit lectus, quis elementum arcu massa sed odio. Quisque pretium felis non lorem porttitor, sed vestibulum purus eleifend. Praesent porttitor congue lorem a maximus. Morbi a placerat nisi. Nulla sit amet dui ornare sem pellentesque volutpat. Quisque quis diam a lorem egestas dictum ut sit amet turpis. Praesent ut convallis libero. Suspendisse dui risus, volutpat in interdum ut, venenatis vitae nunc. Phasellus auctor purus ex, eu laoreet quam efficitur vitae. Donec sed vehicula elit, non consequat elit. Aliquam rhoncus laoreet lorem vitae sodales. Curabitur tristique, urna ac mattis egestas, dui lorem sodales risus, ut interdum felis odio nec nisi. Mauris vestibulum, ante quis mattis bibendum, tellus quam bibendum mi, et laoreet nisl massa a felis. In non sodales eros.

Integer posuere tempor nulla, ac malesuada elit. Praesent quis ultrices diam, posuere faucibus sem. Duis lobortis libero et elit placerat congue. Sed euismod neque ut nunc facilisis pellentesque. Donec elementum, sem ac consectetur bibendum, leo nulla elementum nisi, nec aliquam enim turpis nec tellus. Mauris neque mauris, condimentum et mollis quis, molestie ut urna. Cras suscipit purus neque, sollicitudin scelerisque nunc congue id. Nunc a velit condimentum elit semper faucibus non non arcu. Aliquam erat volutpat.

Nulla ac lacus sed libero gravida eleifend et et nunc. Phasellus porta sodales volutpat. Nam id consequat libero. Sed bibendum sem dolor. Aenean enim nulla, rutrum vitae leo quis, tempus ullamcorper enim. Fusce sed lobortis quam. Class aptent taciti sociosqu ad litora torquent per conubia nostra, per inceptos himenaeos. Duis dapibus ex a condimentum ullamcorper. Nam ut sem aliquam, rhoncus purus ut, lacinia mauris.

Pellentesque id gravida turpis, nec laoreet sem. Maecenas sagittis, purus eget maximus ullamcorper, orci lectus imperdiet quam, at vulputate elit turpis et quam. Etiam tristique, diam sed feugiat consectetur, mi enim ullamcorper metus, vel pretium erat mauris quis purus. Interdum et malesuada fames ac ante ipsum primis in faucibus. Praesent tempor porta tortor, sit amet aliquam magna imperdiet ac. Proin posuere placerat dolor, quis accumsan diam tempus a. Curabitur varius vulputate porttitor. Praesent lectus urna, pharetra ut sem at, euismod elementum ligula. In iaculis justo mauris, nec aliquam est tincidunt ac. Donec malesuada mi tempor leo congue, eget auctor magna eleifend. Nunc ut egestas augue. Integer ullamcorper hendrerit ante, in lacinia felis egestas in. Aenean sodales ante molestie eros tincidunt interdum. Aliquam ligula augue, fermentum ac vehicula ac, posuere ac augue. Proin posuere, sapien in ornare vestibulum, urna urna tempor ex, ut volutpat massa ex ut nulla. Integer fermentum augue nulla, at facilisis nulla volutpat quis.

Duis porta dignissim sem. In pulvinar gravida maximus. Vivamus at tortor eget sapien ultricies bibendum. Nam vestibulum vulputate purus, sit amet auctor diam tincidunt id. Mauris lobortis nisl eget nunc tempus commodo. Pellentesque eleifend, magna non cursus finibus, dui nisl luctus nisl, nec facilisis dui nisi aliquet dolor. Etiam ac odio neque. Proin imperdiet at arcu vitae iaculis. Maecenas at mauris vel libero facilisis efficitur.

Donec eu auctor lacus, sed accumsan lectus. Aenean nec sem non neque dapibus egestas at ultricies leo. Nunc vel nisl dui. Donec nec dolor vel lacus iaculis auctor et in ex. Pellentesque sed cursus tellus, eu ullamcorper ex. Donec luctus in est et bibendum. In hac habitasse platea dictumst. In eleifend enim in ligula laoreet, non bibendum leo convallis. Vestibulum in sapien in orci sodales feugiat sed sit amet metus. Vivamus sit amet nunc eget augue malesuada lobortis. Sed vulputate erat eget dui rhoncus cursus.

Duis finibus quis neque sed dapibus. Aliquam eu lacinia est. Nunc lacinia arcu sed ultricies blandit. Ut quis tincidunt massa, id ornare velit. Duis vel ex eu ante rutrum volutpat in eu purus. Mauris vel dapibus urna, vel sagittis lectus. Quisque posuere erat augue, quis mattis nisl accumsan sit amet. Morbi id erat dui.

Donec ut suscipit mi, malesuada pulvinar velit. Fusce imperdiet, lectus et porttitor porttitor, magna elit lobortis arcu, sodales dictum magna dui non massa. Sed bibendum pulvinar turpis vitae tempus. Mauris venenatis leo ut finibus lacinia. Donec eget porta sapien. Etiam vulputate nibh nec lectus rhoncus consequat. Interdum et malesuada fames ac ante ipsum primis in faucibus. Vivamus id nunc at nunc sagittis maximus non venenatis libero. Integer augue lectus, sagittis vel maximus id, tempus non metus. Nullam eu facilisis dolor. Suspendisse congue arcu id diam pharetra sodales. Duis posuere, dui faucibus rutrum vehicula, tellus dui sodales neque, nec bibendum diam augue ut urna.

Nulla et risus ut enim pretium interdum id ut lorem. Aliquam erat volutpat. Nulla ut sodales justo. Proin suscipit odio at eros convallis molestie. Mauris non imperdiet tellus. Morbi suscipit purus mi, vitae congue libero malesuada in. Nulla turpis magna, sollicitudin sed ultrices a, vulputate at lacus. Pellentesque habitant morbi tristique senectus et netus et malesuada fames ac turpis egestas. Morbi iaculis, nisl eget maximus fermentum, metus dui finibus est, egestas dictum dui nunc in nulla. Integer massa diam, luctus nec augue sit amet, dictum varius dolor. Aenean faucibus egestas mi, non blandit enim.

Ut fermentum, lorem in tempor faucibus, velit nibh maximus ipsum, a vulputate orci risus vitae nisi. Integer ultrices fringilla massa. Curabitur at volutpat eros. Nulla molestie quam sed risus tincidunt rutrum. Fusce gravida mauris quam, nec lobortis erat finibus sit amet. Proin pulvinar suscipit magna, sit amet facilisis velit pulvinar ac. Praesent non ligula et orci porta volutpat feugiat ac leo. Proin eu iaculis metus, at iaculis nulla. In hac habitasse platea dictumst. Nullam bibendum ornare mi sed tincidunt. Cum sociis natoque penatibus et magnis dis parturient montes, nascetur ridiculus mus. Cras sed auctor arcu. In eget ullamcorper nunc.

Nunc tempus ante et convallis fringilla. Nam dolor urna, finibus vel tortor sit amet, pharetra dignissim justo. Vivamus vehicula vitae dolor nec finibus. Maecenas sed condimentum nisi. Quisque vitae tristique elit. Phasellus fermentum augue id lectus tempor pellentesque. In hac habitasse platea dictumst. Cras enim erat, placerat ut velit vel, convallis fringilla elit. Pellentesque mollis diam vitae interdum posuere. Nulla euismod quam sed lacus laoreet ultricies. Sed tincidunt porttitor mi, vitae hendrerit quam. Integer dignissim ante non semper vestibulum. In euismod neque id suscipit mollis. Aliquam faucibus est nunc, at molestie tortor posuere vitae. Curabitur urna tortor, volutpat ac porttitor non, ullamcorper non velit.

Aliquam nec luctus purus, sodales porta risus. Sed iaculis orci vitae quam iaculis, vitae porttitor massa scelerisque. Nulla et gravida felis. Class aptent taciti sociosqu ad litora torquent per conubia nostra, per inceptos himenaeos. In hac habitasse platea dictumst. Etiam vitae ultricies odio. Mauris ut sapien ac neque finibus rutrum. Ut et arcu leo. Curabitur consectetur lacinia neque, at luctus velit dignissim a.

Morbi in mollis sapien. Proin eleifend suscipit condimentum. Donec vel malesuada tellus. Sed venenatis rhoncus turpis, ut venenatis dolor malesuada at. Maecenas dictum pellentesque ex vel placerat. Vivamus sollicitudin orci ac sagittis dapibus. Proin viverra fermentum libero vel pretium. Proin lacinia lectus non nisl pulvinar venenatis. Mauris a euismod dolor. Nam sit amet vulputate mi.

Pellentesque et nibh lorem. Quisque iaculis nisi turpis, et aliquet ex ultricies eget. Nulla ut tellus nisl. Nunc fermentum a lacus at viverra. Aliquam id dui ac lectus ornare feugiat. In lobortis arcu eget nisl facilisis lacinia. Aliquam erat volutpat. Sed vestibulum dignissim justo, nec commodo nisl pharetra non. Vivamus tristique rhoncus nibh et egestas. Nulla facilisi. Morbi et aliquam neque. Cras interdum porta tincidunt.

Donec laoreet diam et justo mattis dapibus. Nam vulputate dictum ex sed ullamcorper. Cras quis convallis quam, in pellentesque felis. Curabitur ut erat tristique, varius erat ac, vehicula ante. Suspendisse potenti. Sed semper neque leo, eu placerat leo tempus sit amet. Vivamus commodo lorem ipsum, a semper nisl fringilla sit amet. Maecenas a suscipit quam. Nulla libero sem, suscipit condimentum finibus nec, tincidunt non diam. Suspendisse convallis metus mattis nisl sagittis, nec auctor ex interdum. Proin at pharetra ipsum. Vestibulum dictum luctus nibh, non sodales augue. Nulla quis erat ut quam lacinia dapibus.

Aliquam eu odio semper, tristique sapien in, tempus nisl. Nam sed sapien at sem volutpat sollicitudin vitae vel nisl. Duis eget dapibus felis, id tincidunt mauris. Proin ornare vitae justo at eleifend. Maecenas posuere pharetra mi id semper. Quisque scelerisque fermentum odio. Praesent in euismod libero. Phasellus ac congue mauris. Sed faucibus ex vitae felis blandit luctus. Nulla elementum ipsum lectus, at ornare mi semper quis. Ut vitae felis ut orci fermentum sagittis varius non sem. Etiam pellentesque, turpis in fringilla maximus, ipsum orci tristique arcu, in semper ipsum enim eget lectus. Sed aliquet metus vel velit dignissim, sed ullamcorper nisi convallis. Aliquam faucibus ullamcorper ex, ut efficitur nibh dictum at. Proin quis vehicula sem. Nulla porta semper quam eu aliquet.

Suspendisse potenti. Pellentesque non ipsum in lectus posuere feugiat ut ac nibh. Donec in blandit ipsum, nec interdum nisl. Aenean congue enim vitae ipsum consectetur, id sollicitudin neque eleifend. Donec et sapien vehicula justo laoreet gravida. Pellentesque non aliquam felis. Nullam vel est nec tortor iaculis dapibus sed non sem. Donec dui sem, malesuada dapibus congue laoreet, egestas at ante. Morbi lobortis sem ultrices enim tempus lacinia. Aliquam ullamcorper ipsum quis dui dapibus, pellentesque pretium tortor lacinia. Sed efficitur pellentesque dui eget hendrerit. Praesent egestas venenatis dui, ac egestas neque venenatis non. Integer eget leo et nibh pellentesque consequat at tincidunt sem. Nunc in blandit erat. Curabitur ornare vehicula posuere.

Quisque et dignissim turpis. Mauris vitae tincidunt nisl. Pellentesque at auctor felis. Nullam sit amet justo quis eros porttitor varius. Nunc bibendum placerat arcu. Suspendisse a mi risus. Morbi vehicula sapien at quam blandit lobortis id a nisi. Phasellus facilisis euismod odio, eu vestibulum orci suscipit et. Aenean nisl massa, ultricies id efficitur eget, imperdiet nec magna. Ut sed quam non lectus consectetur gravida.

Proin dui metus, congue eu pharetra nec, rhoncus a dui. Sed et commodo sapien, id vehicula orci. Sed tempus nibh ut tortor dictum convallis. Morbi tempor nibh et arcu laoreet rutrum. Vestibulum faucibus sem sollicitudin ante pharetra laoreet. Fusce sit amet lobortis nisl. Sed efficitur urna eu nulla viverra tincidunt sed id nisi. Nunc dictum maximus lacus, eget dignissim nunc suscipit sit amet. Nam id sapien lacus. Sed sodales magna in blandit tincidunt. Integer leo felis, rutrum id vestibulum a, tempor vel felis.

Nam accumsan condimentum mauris. Cum sociis natoque penatibus et magnis dis parturient montes, nascetur ridiculus mus. Vivamus varius odio quis ante fringilla tristique. Ut eu varius elit. Morbi tempus quis ligula sed porttitor. Aliquam auctor pulvinar sem, vitae semper enim interdum eu. Sed pretium erat eget justo suscipit, vitae accumsan dolor iaculis. Proin ut augue eleifend dolor tristique ultrices. Proin cursus sodales ipsum.

Vivamus consequat est vel neque accumsan laoreet. Quisque consequat elementum molestie. Donec varius dolor eget velit pulvinar, eu auctor ex consequat. Suspendisse a nulla nec augue rutrum commodo et non arcu. Aliquam id urna vel nisl consectetur interdum. Donec ac ipsum ante. Nam ultricies gravida justo id congue. Suspendisse vitae purus lectus.";
    }
}