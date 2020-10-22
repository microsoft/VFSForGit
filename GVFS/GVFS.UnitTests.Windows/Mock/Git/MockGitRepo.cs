using GVFS.Common;
using GVFS.Common.FileSystem;
using GVFS.Common.Git;
using GVFS.Common.Tracing;
using GVFS.Tests.Should;
using System;
using System.Collections.Generic;

namespace GVFS.UnitTests.Mock.Git
{
    public class MockGitRepo : GitRepo
    {
        private Dictionary<string, MockGitObject> objects = new Dictionary<string, MockGitObject>();
        private string rootSha;

        public MockGitRepo(ITracer tracer, Enlistment enlistment, PhysicalFileSystem fileSystem)
            : base(tracer)
        {
            this.rootSha = Guid.NewGuid().ToString();
            this.AddTree(this.rootSha, ".");
        }

        /// <summary>
        /// Adds an unparented tree to the "repo"
        /// </summary>
        public void AddTree(string sha, string name, params string[] childShas)
        {
            MockGitObject newObj = new MockGitObject(sha, name, false);
            newObj.ChildShas.AddRange(childShas);
            this.objects.Add(sha, newObj);
        }

        /// <summary>
        /// Adds an unparented blob to the "repo"
        /// </summary>
        public void AddBlob(string sha, string name, string contents)
        {
            MockGitObject newObj = new MockGitObject(sha, name, true);
            newObj.Content = contents;
            this.objects.Add(sha, newObj);
        }

        /// <summary>
        /// Adds a child sha to an existing tree
        /// </summary>
        public void AddChildBySha(string treeSha, string childSha)
        {
            MockGitObject treeObj = this.GetTree(treeSha);
            treeObj.ChildShas.Add(childSha);
        }

        /// <summary>
        /// Adds an parented blob to the "repo"
        /// </summary>
        public string AddChildBlob(string parentSha, string childName, string childContent)
        {
            string newSha = Guid.NewGuid().ToString();
            this.AddBlob(newSha, childName, childContent);
            this.AddChildBySha(parentSha, newSha);
            return newSha;
        }

        /// <summary>
        /// Adds an parented tree to the "repo"
        /// </summary>
        public string AddChildTree(string parentSha, string name, params string[] childShas)
        {
            string newSha = Guid.NewGuid().ToString();
            this.AddTree(newSha, name, childShas);
            this.AddChildBySha(parentSha, newSha);
            return newSha;
        }

        public string GetHeadTreeSha()
        {
            return this.rootSha;
        }

        public override bool TryGetBlobLength(string blobSha, out long size)
        {
            MockGitObject obj;
            if (this.objects.TryGetValue(blobSha, out obj))
            {
                obj.IsBlob.ShouldEqual(true);
                size = obj.Content.Length;
                return true;
            }

            size = 0;
            return false;
        }

        private MockGitObject GetTree(string treeSha)
        {
            this.objects.ContainsKey(treeSha).ShouldEqual(true);
            MockGitObject obj = this.objects[treeSha];
            obj.IsBlob.ShouldEqual(false);
            return obj;
        }

        private class MockGitObject
        {
            public MockGitObject(string sha, string name, bool isBlob)
            {
                this.Sha = sha;
                this.Name = name;
                this.IsBlob = isBlob;
                this.ChildShas = new List<string>();
            }

            public string Sha { get; private set; }
            public string Name { get; set; }
            public bool IsBlob { get; set; }
            public List<string> ChildShas { get; set; }
            public string Content { get; set; }
        }
    }
}
