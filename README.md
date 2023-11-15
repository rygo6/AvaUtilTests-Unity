# AvaUtilTests-Unity

Abondoned utility project for avatars. Decided to not use unity for this so anything in here is public domain if you find it useful.

Has a few different implementations of AO baking in Burst jobs and Compute Shader. Had one novel solution where it rendered the object being baked from the view of each of its own vertices with a single draw call into a grid through instancing. This was fast enough to do vert baking near realtime on my test meshes. 

https://www.youtube.com/watch?v=oX_QQzQiZL4

Has a shader implementation of baking the SDF of a spline onto mesh via shader.

![SplineBake](SplineBake.gif)

Has a Burst implementation of baking the 'Vertex Flow' of an avatar mesh. You specif a point, like the head, and it will walk vertex to vertex recording the direction it walked into the vertex color.

![MeshWalk](MeshWalk.gif)
